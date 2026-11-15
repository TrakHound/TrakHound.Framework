// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Blazored.LocalStorage;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging.Console;
using NLog;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using NLog.Web;
using Radzen;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using TrakHound.Apps;
using TrakHound.Blazor;
using TrakHound.Blazor.Routing;
using TrakHound.Blazor.Services;
using TrakHound.Clients;
using TrakHound.Deployments;
using TrakHound.Http;
using TrakHound.Instance.Api;
using TrakHound.Instance.Apps;
using TrakHound.Instance.Boots;
using TrakHound.Instance.Configurations;
using TrakHound.Instance.Health;
using TrakHound.Instance.Http;
using TrakHound.Instance.Logging;
using TrakHound.Instance.Logging.History;
using TrakHound.Instance.Mcp;
using TrakHound.Instance.Notifications;
using TrakHound.Instance.PackageSources;
using TrakHound.Instance.Security;
using TrakHound.Instances;
using TrakHound.Logging;
using TrakHound.Security;

#if !DEBUG
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
#endif

namespace TrakHound.Instance
{
    /// <summary>
    /// Runs an Instance: sets up Logging, the Startup files (Deployments and the Admin User), the TrakHound Instance, and the ASP.NET Core
    /// host that serves it. Each step is a method, in the order they run (see Run)
    /// </summary>
    public class TrakHoundInstanceHost
    {
        // Console Log layout : "2026-09-30 21:27:09.034  INFO   [api/TrakHound.Packages.Api]  Message"
        // (the readable Logger Name is a property since the Logger Name itself is the Logger ID used for the Log File directory)
        private const string _consoleLayout = "${date:format=yyyy-MM-dd HH\\:mm\\:ss.fff}  ${pad:padding=-5:inner=${level:uppercase=true}}  [${event-properties:item=" + TrakHoundNLogProvider.LoggerNameProperty + ":whenEmpty=${logger:shortName=true}}]  ${message}${onexception:${newline}${exception:format=tostring}}";

        // Allow time for the Instance to Stop (Workers can take up to 30 seconds each, Stopped in parallel)
        private static readonly TimeSpan _shutdownTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan _webSocketCloseTimeout = TimeSpan.FromSeconds(5);

        // The largest message a browser can send over a Blazor connection (Admin Interface and Apps). File uploads (InputFile) are
        // streamed in smaller chunks, so this only needs to fit JS interop calls (ex. the text of an editor)
        private const long _maxBlazorMessageSize = 4 * 1024 * 1024;

        private readonly string _configurationProfileId;
        private readonly string _configurationRootPath;
        private readonly string _configurationPath;
        private readonly string _packagePath;
        private readonly string _volumePath;
        private readonly TrakHoundStartupDeployments _startupDeployments;

        private Logger _logger;
        private TrakHoundLogConfiguration _logHistoryConfiguration;
        private string _logHistoryConfigurationPath;
        private ITrakHoundLogStore _logStore;
        private TrakHoundInstance _instance;
        private TrakHoundHttpWebSocketManager _webSocketManager;
        private bool _instanceStopped;


        /// <param name="configurationProfileId">The Configuration Profile (a directory in the Configuration Path)</param>
        /// <param name="configurationRootPath">The Configuration Path (the parent of the Profile directories)</param>
        public TrakHoundInstanceHost(string configurationProfileId, string configurationRootPath, string packagePath, string volumePath, TrakHoundStartupDeployments startupDeployments)
        {
            _configurationProfileId = configurationProfileId;
            _configurationRootPath = configurationRootPath;
            _configurationPath = Path.Combine(configurationRootPath, configurationProfileId);
            _packagePath = packagePath;
            _volumePath = volumePath;
            _startupDeployments = startupDeployments;
        }


        public async Task Run()
        {
            ConfigureLogging();

            // Recorded in the Boot History (how long each step took, and the Warnings and Errors logged until Ready)
            var bootSteps = new TrakHoundBootStepTimer();
            TrakHoundBootLogCollector bootLog = null;

            try
            {
                bootSteps.Begin("Read Configuration");
                var startupAdminUser = ApplyStartupFiles();
                var instanceConfiguration = ReadInstanceConfiguration();

                bootSteps.Begin("Create Host");
                var builder = CreateBuilder();
                ConfigureLoggingProviders(builder);

                bootSteps.Begin("Initialize Instance");
                var logProvider = new TrakHoundNLogProvider(instanceConfiguration.InstanceId, _logger, _logStore);
                bootLog = new TrakHoundBootLogCollector(logProvider);

                // Created before the Services are added, since many of them use it
                _instance = new TrakHoundInstance(instanceConfiguration, logProvider, _configurationPath, _packagePath, _volumePath);

                // Admin Roles that are limited to Zones are checked against the running Instance's Zones
                TrakHoundAdminScope.ZoneManager = () => _instance.ZoneManager;
                _instance.RuntimeStartCompleted += (s, start) => TrakHoundInstanceLifetimeMonitor.OnRuntimeStart(start);
                _instance.Stopped += (s, args) => TrakHoundInstanceLifetimeMonitor.OnRuntimeStopped();
                await _instance.Initialize();

                // Include the Log History directory in the Health Disk Check
                _instance.Health.AddDiskPath("Logs", _logHistoryConfiguration?.GetDirectory());

                bootSteps.Begin("Configure Services");
                AddInstanceServices(builder, instanceConfiguration, startupAdminUser);
                ConfigureServer(builder, instanceConfiguration);

                var app = builder.Build();
                _instance.ServiceProvider = app.Services;

                UseInstancePipeline(app);
                MapEndpoints(app);

                // Close WebSockets when Stopping begins (before the Server waits for open requests to complete)
                var applicationLifetime = app.Services.GetService<IHostApplicationLifetime>();
                applicationLifetime.ApplicationStopping.Register(TrakHoundInstanceLifetimeMonitor.OnApplicationStopping);
                applicationLifetime.ApplicationStopping.Register(CloseWebSockets);

                bootSteps.Begin("Start HTTP Server");
                await app.StartAsync();
                bootSteps.End();

                // Recorded as the Boot's first Runtime Start
                await _instance.Start();

                TrakHoundInstanceLifetimeMonitor.OnBootReady(bootSteps.Steps, bootLog.GetLog());
                bootLog.Dispose();

                await app.WaitForShutdownAsync();
            }
            catch (Exception exception)
            {
                //NLog: catch setup errors
                _logger.Fatal(exception, "Stopped program because of exception");
                bootSteps.End(exception.Message);
                TrakHoundInstanceLifetimeMonitor.OnRunException(exception, bootSteps.Steps, bootLog?.GetLog());
                throw;
            }
            finally
            {
                bootLog?.Dispose();

                // Normally Stopped by TrakHoundInstanceShutdownService during Host shutdown. This covers a failed startup
                if (!_instanceStopped && _instance != null) await StopInstance();

                TrakHoundInstanceLifetimeMonitor.OnShutdownComplete();

                // Ensure to flush and stop internal timers/threads before application-exit (Avoid segmentation fault on Linux)
                LogManager.Shutdown();

                // Close Log History (after NLog has flushed the last entries to it)
                try { _logStore?.Dispose(); } catch { }
            }
        }


        #region "Logging"

        /// <summary>
        /// NLog (from nlog.config in the Configuration Profile, created from nlog.default.config), the TrakHound Console layout, and Log History
        /// </summary>
        private void ConfigureLogging()
        {
            var logConfigurationPath = Path.Combine(_configurationPath, "nlog.config");
            var logDefaultConfigurationPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "nlog.default.config");
            if (!File.Exists(logConfigurationPath) && File.Exists(logDefaultConfigurationPath))
            {
                var directoryName = Path.GetDirectoryName(logConfigurationPath);
                if (!Directory.Exists(directoryName)) Directory.CreateDirectory(directoryName);

                // Copy Default NLog Configuration File
                File.Copy(logDefaultConfigurationPath, logConfigurationPath);
            }
            _logger = LogManager.Setup().LoadConfigurationFromFile(logConfigurationPath, true).GetCurrentClassLogger();

            // Apply the TrakHound Console layout (also when nlog.config is reloaded)
            ApplyConsoleLayout(LogManager.Configuration);
            LogManager.ConfigurationChanged += (s, args) => ApplyConsoleLayout(args.ActivatedConfiguration);

            // Set Log History (what the Logs API and Explorer query)
            _logHistoryConfigurationPath = Path.Combine(_configurationPath, TrakHoundLogConfiguration.Filename);
            var logHistoryMessages = new List<LogEventInfo>(); // Logged once Log History is attached (so they are also in Log History)
            _logHistoryConfiguration = ReadLogHistoryConfiguration(_logHistoryConfigurationPath, logHistoryMessages);
            _logStore = CreateLogStore(_logHistoryConfiguration, logHistoryMessages);
            ApplyLogHistory(LogManager.Configuration, true);
            foreach (var message in logHistoryMessages) _logger.Log(message);
            LogManager.ConfigurationChanged += (s, args) => ApplyLogHistory(args.ActivatedConfiguration, false);

            // Log when and why the Instance Stops or Crashes
            TrakHoundInstanceLifetimeMonitor.Initialize(_logger, _configurationPath);
        }

        private static void ConfigureLoggingProviders(WebApplicationBuilder builder)
        {
            builder.Host.UseNLog();

            // Format ASP.NET Core Console messages (ex. "Now listening on") to match the TrakHound Console layout
            builder.Logging.AddConsoleFormatter<TrakHoundConsoleFormatter, ConsoleFormatterOptions>();
            builder.Logging.AddConsole(o => o.FormatterName = TrakHoundConsoleFormatter.FormatterName);
        }

        /// <summary>
        /// Read the Log History Configuration File (the default file, with comments, is created if it doesn't exist)
        /// </summary>
        private TrakHoundLogConfiguration ReadLogHistoryConfiguration(string path, List<LogEventInfo> messages)
        {
            try
            {
                var configuration = TrakHoundLogConfiguration.Read(path);
                if (configuration != null)
                {
                    messages.Add(CreateLogEvent(NLog.LogLevel.Info, $"Log Configuration File Read : {path}"));
                    return configuration;
                }

                messages.Add(CreateLogEvent(NLog.LogLevel.Info, "No Log Configuration File Found. Loading Default.."));
                TrakHoundLogConfiguration.WriteDefault(path);
            }
            catch (Exception ex)
            {
                messages.Add(CreateLogEvent(NLog.LogLevel.Error, $"Error Reading Log Configuration File ({path}) : {ex.Message}. Loading Default.."));
            }

            return new TrakHoundLogConfiguration();
        }

        private ITrakHoundLogStore CreateLogStore(TrakHoundLogConfiguration configuration, List<LogEventInfo> messages)
        {
            var directory = configuration.GetDirectory();

            try
            {
                ITrakHoundLogStore store = configuration.ProviderType == TrakHoundLogStorageProvider.Sqlite
                    ? new TrakHoundSqliteLogStore(directory)
                    : new TrakHoundFileLogStore(directory);

                messages.Add(CreateLogEvent(NLog.LogLevel.Info, $"Log History : Provider = {configuration.ProviderType} : Path = {directory} : Minimum Level = {configuration.MinimumLogLevel} : Retention = {(configuration.RetentionDays > 0 ? $"{configuration.RetentionDays} days" : "None")} : Archive Mode = {configuration.ArchiveModeType}"));
                return store;
            }
            catch (Exception ex)
            {
                messages.Add(CreateLogEvent(NLog.LogLevel.Error, $"Error Creating {configuration.ProviderType} Log History ({directory})", ex));

                if (configuration.ProviderType != TrakHoundLogStorageProvider.File)
                {
                    try
                    {
                        messages.Add(CreateLogEvent(NLog.LogLevel.Warn, "Log History : Using the File provider instead"));
                        return new TrakHoundFileLogStore(directory);
                    }
                    catch { }
                }
            }

            return null;
        }

        private LogEventInfo CreateLogEvent(NLog.LogLevel level, string message, Exception exception = null)
        {
            return new LogEventInfo(level, _logger.Name, message) { Exception = exception };
        }

        /// <summary>
        /// Add the Log History Target to NLog (also when nlog.config is reloaded)
        /// </summary>
        private void ApplyLogHistory(LoggingConfiguration configuration, bool logChanges)
        {
            if (configuration == null || _logStore == null) return;

            var messages = TrakHoundLogHistoryTarget.Apply(configuration, _logStore, _logHistoryConfiguration);
            LogManager.ReconfigExistingLoggers();

            if (logChanges)
            {
                foreach (var message in messages) _logger?.Info(message);
            }
        }

        /// <summary>
        /// Use the TrakHound layout for Console Targets that use NLog's default layout (ex. an nlog.config created by an earlier version).
        /// Console Targets with a custom layout are not changed.
        /// </summary>
        private static void ApplyConsoleLayout(LoggingConfiguration configuration)
        {
            if (configuration == null) return;

            try
            {
                var changed = false;

                foreach (var target in configuration.AllTargets)
                {
                    if (target is ConsoleTarget consoleTarget && IsDefaultLayout(consoleTarget.Layout, new ConsoleTarget().Layout))
                    {
                        consoleTarget.Layout = _consoleLayout;
                        changed = true;
                    }
                    else if (target is ColoredConsoleTarget coloredConsoleTarget && IsDefaultLayout(coloredConsoleTarget.Layout, new ColoredConsoleTarget().Layout))
                    {
                        coloredConsoleTarget.Layout = _consoleLayout;
                        changed = true;
                    }
                }

                if (changed) LogManager.ReconfigExistingLoggers();
            }
            catch { }
        }

        private static bool IsDefaultLayout(Layout layout, Layout defaultLayout)
        {
            return layout is SimpleLayout simpleLayout && defaultLayout is SimpleLayout defaultSimpleLayout && simpleLayout.OriginalText == defaultSimpleLayout.OriginalText;
        }

        #endregion

        #region "Startup"

        /// <summary>
        /// Apply the files left for this Instance (ex. by the Installer) before anything is read
        /// </summary>
        private TrakHoundStartupAdminUser ApplyStartupFiles()
        {
            // Deployments first, since they can change the Instance Configuration, Variables, Packages and Volumes
            _startupDeployments.Install(new TrakHoundDeploymentInstallOptions
            {
                ConfigurationPath = _configurationRootPath,
                PackagesPath = _packagePath,
                VolumesPath = new TrakHound.Volumes.TrakHoundVolumeProvider(_volumePath).BasePath,
                VariablesPath = new TrakHound.Variables.TrakHoundVariableProvider().BasePath,
                InformationPath = TrakHoundStartupDeployments.InformationPath,
                BackupPath = TrakHoundStartupDeployments.BackupPath
            }, LogManager.GetLogger("TrakHound.Instance.Deployments"));

            // The first Admin Account (from the Installer, or a Password file). Before the Admin Interface is served, since the Admin Interface can't create it
            return TrakHoundStartupAdminUser.Apply(_configurationPath, LogManager.GetLogger("TrakHound.Instance.Security"));
        }

        private TrakHoundInstanceConfiguration ReadInstanceConfiguration()
        {
            // This Instance's own files moved out of the 'security' folder (which Deployments copy) into '.local'
            foreach (var moved in TrakHoundInstanceLocalFiles.MoveFromPreviousLocation(_configurationPath))
            {
                _logger.Info($"Moved '{moved}' to the '{TrakHound.Deployments.TrakHoundDeployment.LocalDirectory}' folder of the Configuration Profile");
            }

            // Settings can reference Variables (ex. "httpPort: ${port}"), so a deployment for many devices can give each its own values
            TrakHound.Configurations.TrakHoundConfigurationVariables.Load(_configurationPath);

            var instanceConfigurationPath = Path.Combine(_configurationPath, TrakHoundInstanceConfiguration.Filename);
            var instanceConfiguration = TrakHoundInstanceConfiguration.Read(instanceConfigurationPath, value => TrakHound.Configurations.TrakHoundConfigurationVariables.Resolve(value));
            if (instanceConfiguration != null)
            {
                _logger.Info($"Instance Configuration File Read : {instanceConfigurationPath}");
                foreach (var reference in instanceConfiguration.UnresolvedReferences)
                {
                    _logger.Warn($"Instance Setting not resolved, so its default is used : {reference}. Define the Variable (or Environment Variable) it references");
                }

                // The Instance ID is kept in its own file. One saved in the settings (by an earlier version) moves there, and the settings are saved without it
                TrakHoundInstanceIdentity.Load(_configurationPath, instanceConfiguration);
                if (!string.IsNullOrEmpty(instanceConfiguration.FileInstanceId))
                {
                    instanceConfiguration.Save(instanceConfigurationPath, false);
                    instanceConfiguration.FileInstanceId = null;
                }

                return instanceConfiguration;
            }

            _logger.Info("No Instance Configuration File Found. Loading Default..");
            instanceConfiguration = new TrakHoundInstanceConfiguration();
            TrakHoundInstanceIdentity.Load(_configurationPath, instanceConfiguration);
            instanceConfiguration.Save(instanceConfigurationPath, false);
            instanceConfiguration.Path = instanceConfigurationPath; // Where Settings are saved
            return instanceConfiguration;
        }

        private static WebApplicationBuilder CreateBuilder()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions()
            {
                ContentRootPath = WindowsServiceHelpers.IsWindowsService() || SystemdHelpers.IsSystemdService() ? AppContext.BaseDirectory : default
            });

#if DEBUG
            // Serve Static Web Assets (_content/*, _framework/*) from the referenced projects even when not running
            // in the Development environment (ex. no launchSettings.json, since Properties/ is not committed)
            builder.WebHost.UseStaticWebAssets();
#endif

            if (!IsRunningInProcessIIS() && !IsRunningInDocker())
            {
                // Each does nothing unless the Instance runs under that service manager
                builder.Host.UseWindowsService();
                builder.Host.UseSystemd();
            }

            return builder;
        }

        #endregion

        #region "Services"

        private void AddInstanceServices(WebApplicationBuilder builder, TrakHoundInstanceConfiguration instanceConfiguration, TrakHoundStartupAdminUser startupAdminUser)
        {
            var services = builder.Services;

            // Add Local Storage (using Blazored)
            services.AddBlazoredLocalStorage();
            services.AddTransient<ITrakHoundAppLocalStorageService, TrakHoundLocalStorageService>();

            // Add Blazor
            services.AddRadzenComponents();
            services.AddRazorComponents((options) => options.DetailedErrors = builder.Environment.IsDevelopment())
                .AddInteractiveServerComponents(options =>
                {
                    // Exception details are sent to the browser, so only in Development
                    options.DetailedErrors = builder.Environment.IsDevelopment();
                    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(1);
                    options.DisconnectedCircuitMaxRetained = 50;
                });
            services.AddSignalR(options =>
            {
                options.MaximumReceiveMessageSize = _maxBlazorMessageSize;

                // So the App Connection has the Client's Request whichever Transport the Circuit uses
                options.AddFilter<TrakHoundAppHubFilter>();
            });

            // Log App Page exceptions with the Request ID that AppException shows (registered after Blazor's, so it replaces it)
            services.AddScoped<Microsoft.AspNetCore.Components.Web.IErrorBoundaryLogger, Apps.TrakHoundErrorBoundaryLogger>();

            // Don't match the Admin Interface Pages when it is disabled (the Razor Components are still mapped for the Apps)
            services.AddSingleton<Microsoft.AspNetCore.Routing.MatcherPolicy>(new AdminInterfaceMatcherPolicy(instanceConfiguration.AdminInterfaceEnabled));

            // Log unhandled Request errors with the Request ID shown on the Error Page
            services.AddExceptionHandler<TrakHoundExceptionLogger>();

            // Admin Interface Page Services
            services.AddScoped<Pages.Configuration.ConfigurationsService>();
            services.AddSingleton<Pages.Configuration.ConfigurationWorkspaces>();
            services.AddSingleton<TrakHoundInstancePageRouteManager>();
            services.AddScoped<Components.AreaBreadcrumbService>();
            services.AddScoped<Pages.Explorer.ExplorerService>();
            services.AddScoped<Pages.Explorer.ExplorerTreeViewService>();
            services.AddScoped<Pages.Explorer.Services.Messages.View.ClientService>();
            services.AddSingleton<TrakHoundDownloadService>();
            services.AddSingleton<Pages.Deployments.DeploymentsService>();
            services.AddScoped<Blazor.Services.ContextMenuService>();
            services.AddScoped<JavascriptService>();
            services.AddSingleton<Pages.Explorer.InstanceManager>();

            // The Instance
            services.AddSingleton<TrakHoundInstanceConfiguration>(instanceConfiguration);
            services.AddSingleton<ITrakHoundInstance>(_instance);
            services.AddSingleton<ITrakHoundClientProvider>(_instance.ClientProvider);

            // Add Notifications (Admin Interface Notifications sidebar)
            var notificationService = new TrakHoundInstanceNotificationService(_configurationPath, LogManager.GetLogger("TrakHound.Instance.Notifications"));
            services.AddSingleton(notificationService);
            services.AddScoped<Pages.Notifications.NotificationsSidebarState>();
            TrakHoundStartupNotifications.NotifyPreviousRun(notificationService, _instance.Id);
            TrakHoundStartupNotifications.NotifyStartupDeployments(notificationService, _startupDeployments, _instance.Id);
            TrakHoundStartupNotifications.NotifyStartupAdminUser(notificationService, startupAdminUser, _instance.Id);
            services.AddSingleton(_startupDeployments);
            services.AddSingleton(new TrakHoundConfigurationNotifier(_instance.ConfigurationProfile, notificationService));
            services.AddSingleton(new TrakHoundHealthNotifier(_instance, notificationService));
            services.AddSingleton(new TrakHoundPackageVerificationNotifier(_instance.PackageManager, notificationService));

            // Created now (not on first use) so that CPU and Memory history is collected from startup
            services.AddSingleton(TrakHoundProcessMonitor.Default);

            // Add HttpContext to Blazor Pages
            services.AddHttpContextAccessor();

            // Add Client Connection Information Service
            services.AddSingleton(_instance.HttpRequests);
            services.AddSingleton<TrakHoundAppConnectionManager>();
            services.AddScoped<TrakHoundAppConnectionService>();

            // Measure the CPU used by each App (and the Admin Interface) in its Blazor Circuits
            services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, TrakHoundAppCpuCircuitHandler>();
            services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, TrakHound.Instance.Audit.TrakHoundAuditCircuitHandler>();

            // Add Security Services
            services.AddSingleton<ITrakHoundSecurityManager>(_instance.SecurityManager);
            services.AddSingleton<TrakHoundIdentityStateStore>();
            services.AddScoped<TrakHoundAuthenticationService>();
            AddSessions(services);

            // Add Admin Authentication Services
            // The Roles first, since the Accounts hold them
            services.AddSingleton(new TrakHoundAdminRoleStore(TrakHoundAdminRoleStore.GetDirectory(_configurationPath)));
            var adminAccounts = new TrakHoundAdminAccountStore(TrakHoundAdminAccountStore.GetDirectory(_configurationPath));
            services.AddSingleton(adminAccounts);
            services.AddSingleton(new TrakHoundAdminSessions(_configurationPath, adminAccounts));
            services.AddSingleton(new TrakHoundSignInThrottle());
            services.AddScoped<AdminAuthenticationService>();
            services.AddScoped<AdminAccess>();

            // Rate Limits for the Apis, System API and MCP Tools (read from the Instance Configuration on every Request, so saved changes apply immediately)
            services.AddSingleton(new TrakHoundRateLimiter(() => _instance.Configuration?.RateLimits));

            // Remotes (Package Sources), and the Package Update check (Notifications)
            var remotesConfiguration = ReadRemotesConfiguration();
            services.AddSingleton(remotesConfiguration);
            services.AddHostedService(o => new TrakHoundPackageUpdateNotifier(_instance, remotesConfiguration, notificationService, LogManager.GetLogger("TrakHound.Instance.Notifications")));
            AddPackageManagerServices(services, remotesConfiguration, notificationService);

            // Add TrakHound Blazor Services (Apps)
            services.AddTrakHoundHosting(_instance);

            // Set Default Admin Theme
            TrakHoundThemeService.SetDefaultTheme(AdminThemes.Key, instanceConfiguration.AdminDefaultTheme.ConvertEnum<TrakHoundThemes>());

            // Add WebSocket Manager
            _webSocketManager = new TrakHoundHttpWebSocketManager(_instance.StreamMetrics);
            services.AddSingleton<TrakHoundHttpWebSocketManager>(_webSocketManager);

            // Stop the Instance during Host shutdown (awaited by the Host)
            services.AddHostedService(o => new TrakHoundInstanceShutdownService(StopInstance));
            services.Configure<HostOptions>(o => o.ShutdownTimeout = _shutdownTimeout);

            // Apply Log History Retention and Archiving, and the Log History Settings (Admin Interface)
            TrakHoundLogMaintenanceService logMaintenance = null;
            if (_logStore != null)
            {
                logMaintenance = new TrakHoundLogMaintenanceService(_logStore, _logHistoryConfiguration, _logHistoryConfigurationPath, LogManager.GetLogger("TrakHound.Instance.LogHistory"));
                services.AddHostedService(o => logMaintenance);
            }
            services.AddSingleton(new TrakHoundLogHistoryService(_logHistoryConfiguration, _logHistoryConfigurationPath, _logStore, logMaintenance));

            services.AddControllers()
                .AddJsonOptions(options =>
                {
                    // Not indented (large Log and Metrics responses are much smaller without the whitespace)
                    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault;
                });

            // MCP Server (Model Context Protocol) at /_mcp (served when Enabled, see TrakHoundSystemApiSwitchMiddleware)
            services.AddTrakHoundMcp(_instance);

            // Don't add Compression when debugging. Messes up the CSS Hot Reload
#if !DEBUG
            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<GzipCompressionProvider>();
            });

            services.Configure<GzipCompressionProviderOptions>(options =>
            {
                options.Level = CompressionLevel.Optimal;
            });
#endif
        }

        /// <summary>
        /// The Session Cookies : one created when signing in with an Identity Provider, and the Admin Interface Session
        /// </summary>
        private void AddSessions(IServiceCollection services)
        {
            // Persist the keys that encrypt the Session Cookies so that Sessions survive restarts (and work in Docker, where the default key storage is not persisted)
            // Kept in the Profile's '.local' folder, so a Deployment never copies them to another Instance
            var keysDirectory = new DirectoryInfo(TrakHoundInstanceLocalFiles.GetDataProtectionPath(_instance.ConfigurationProfile.Path));
            var dataProtection = services.AddDataProtection().SetApplicationName("TrakHound.Instance").PersistKeysToFileSystem(keysDirectory);
            if (OperatingSystem.IsWindows())
            {
                dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
            }
            else if (!TrakHoundFilePermissions.RestrictDirectory(keysDirectory.FullName))
            {
                // The keys aren't encrypted on Linux and macOS, so only the Instance's user should be able to read them
                _logger.Warn($"Could not limit {keysDirectory.FullName} to the Instance's user. Anyone who can read it can create Session Cookies");
            }

            // Cookies are shared by every port on a host, so the name includes the Instance ID
            var cookieId = new string((_instance.Id ?? "").Where(o => char.IsLetterOrDigit(o) || o == '-').ToArray());

            services.AddAuthentication(TrakHoundIdentityClaims.AuthenticationScheme)
                .AddCookie(TrakHoundIdentityClaims.AuthenticationScheme, options =>
                {
                    options.Cookie.Name = $"TrakHound.Identity.{cookieId}";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                    options.ExpireTimeSpan = TimeSpan.FromHours(8);
                    options.SlidingExpiration = false;

                    // Challenges are handled by the TrakHoundAuthenticationMiddleware (never redirect to a default Login page)
                    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
                    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
                })
                // The Admin Interface Session (see TrakHoundAdminSessions). Strict, since it is only needed by the Instance's own pages
                // and their Blazor connection (which are always same site), so another site can never send it
                .AddCookie(TrakHoundAdminSessions.AuthenticationScheme, options =>
                {
                    options.Cookie.Name = $"TrakHound.Admin.{cookieId}";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.Strict;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                    options.ExpireTimeSpan = TrakHoundAdminSessions.SessionLifetime;
                    options.SlidingExpiration = false;

                    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
                    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
                });
        }

        /// <summary>
        /// For the Package Sources that are TrakHound Package Managers : report the installed Packages to them, trust the publishers they verified,
        /// and read their attestation lists (for the RequireApproval Verification Mode)
        /// </summary>
        private void AddPackageManagerServices(IServiceCollection services, RemotesConfiguration remotesConfiguration, TrakHoundInstanceNotificationService notificationService)
        {
            var logger = LogManager.GetLogger("TrakHound.Instance.PackageSources");

            var publisherSync = new TrakHoundPackageManagerPublisherSync(_instance.ConfigurationProfile.Path, remotesConfiguration, logger);
            var approvalSync = new TrakHoundPackageManagerApprovalSync(_instance.ConfigurationProfile.Path, remotesConfiguration, logger);
            var inventoryReporter = new TrakHoundPackageInventoryReporter(_instance, remotesConfiguration, logger);

            // Set before the Instance Starts, so that Packages are checked against the last copy when they're loaded
            _instance.AdditionalTrustedPublishersProvider = publisherSync.GetPublishers;
            _instance.PackageApprovalsProvider = approvalSync.GetApprovals;
            _instance.PackageApprovalsRefresh = approvalSync.RefreshBeforeInstall;

            publisherSync.Changed += (s, e) => VerifyPackagesAgain(inventoryReporter, logger, "Publishers");
            approvalSync.Changed += (s, e) =>
            {
                // Only the RequireApproval Mode uses the lists
                if (_instance.PackageManager?.VerificationMode == TrakHound.Packages.TrakHoundPackageVerificationMode.RequireApproval) VerifyPackagesAgain(inventoryReporter, logger, "Attestations");
            };

            // A different server key or an expired list, in the Notifications sidebar until it's resolved
            approvalSync.Problem += (kind, key, title, message) =>
            {
                var existing = notificationService.Find(o => o.Key != null && o.Key.StartsWith(kind));
                if (existing != null && existing.Key == key) return;
                if (existing != null) notificationService.Remove(o => o.Id == existing.Id);

                if (title != null) notificationService.Notify(TrakHoundInstanceNotificationType.Warning, TrakHoundInstanceNotificationCategories.Packages, title, message, "_admin/security/trusted-publishers", "View Package Managers", key);
            };

            services.AddSingleton(publisherSync);
            services.AddSingleton(approvalSync);
            services.AddSingleton(inventoryReporter);
            services.AddHostedService(o => publisherSync);
            services.AddHostedService(o => approvalSync);
            services.AddHostedService(o => inventoryReporter);
        }

        private void VerifyPackagesAgain(TrakHoundPackageInventoryReporter inventoryReporter, Logger logger, string changed)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    // Packages loaded while the Instance Starts are checked with what it read then, so check them again once it has
                    while (_instance.Status == TrakHoundInstanceStatus.Initializing || _instance.Status == TrakHoundInstanceStatus.Starting) await Task.Delay(1000);

                    if (_instance.Status == TrakHoundInstanceStatus.Started)
                    {
                        _instance.PackageManager.VerifyAll();
                        inventoryReporter.NotifyChanged();
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn($"Packages couldn't be checked against the changed Package Manager {changed} : {ex.Message}");
                }
            });
        }

        private RemotesConfiguration ReadRemotesConfiguration()
        {
            var remotesConfigurationPath = Path.Combine(_configurationPath, RemotesConfiguration.Filename);
            var remotesConfiguration = RemotesConfiguration.Read(remotesConfigurationPath);
            if (remotesConfiguration != null)
            {
                _logger.Info($"Remotes Configuration File Read : {remotesConfigurationPath}");
                return remotesConfiguration;
            }

            _logger.Info("No Remotes Configuration File Found. Loading Default..");
            remotesConfiguration = RemotesConfiguration.GetDefault();
            remotesConfiguration.Save(remotesConfigurationPath);
            return remotesConfiguration;
        }

        #endregion

        #region "Server"

        /// <summary>
        /// The Ports the Instance listens on (when it hosts its own HTTP server), including the separate Admin Port
        /// </summary>
        private void ConfigureServer(WebApplicationBuilder builder, TrakHoundInstanceConfiguration instanceConfiguration)
        {
            // Request bodies are limited per Request by TrakHoundRequestBodyLimitMiddleware (so a saved limit applies immediately).
            // IIS still applies requestLimits/maxAllowedContentLength from web.config
            if (IsRunningInProcessIIS())
            {
                builder.Services.Configure<IISServerOptions>(o => o.MaxRequestBodySize = null);
            }

            if (IsRunningInProcessIIS() || IsRunningInDocker())
            {
                // The listening Ports are set by the host (ex. IIS or ASPNETCORE_URLS in a Container)
                if (TrakHoundAdminPort.IsConfigured(instanceConfiguration))
                {
                    _logger.Warn("Admin Port Not Started : The Admin Port is only supported when the Instance hosts its own HTTP server (not IIS or Docker). The Admin Interface is served on the HTTP Port");
                }
                return;
            }

            builder.WebHost.UseKestrel(o =>
            {
                o.Limits.MaxRequestBodySize = null;

                if (!string.IsNullOrEmpty(instanceConfiguration.HttpAddress))
                {
                    if (IPAddress.TryParse(instanceConfiguration.HttpAddress, out var httpAddress))
                    {
                        o.Listen(httpAddress, instanceConfiguration.HttpPort);
                    }
                    else
                    {
                        // Only this computer, rather than every address (the address was set to limit who can connect), so the Settings can be fixed
                        _logger.Error($"Invalid HTTP Address ({instanceConfiguration.HttpAddress}) : Listening on localhost only. Correct the Address in Settings > Interfaces, then restart the Instance");
                        o.ListenLocalhost(instanceConfiguration.HttpPort);
                    }
                }
                else
                {
                    o.ListenAnyIP(instanceConfiguration.HttpPort);
                }

                // Separate Admin Port (Admin Interface and System API)
                if (TrakHoundAdminPort.IsConfigured(instanceConfiguration))
                {
                    var adminAddress = !string.IsNullOrEmpty(instanceConfiguration.AdminHttpAddress) ? instanceConfiguration.AdminHttpAddress : instanceConfiguration.HttpAddress;
                    if (!string.IsNullOrEmpty(adminAddress))
                    {
                        if (IPAddress.TryParse(adminAddress, out var adminHttpAddress))
                        {
                            o.Listen(adminHttpAddress, instanceConfiguration.AdminHttpPort);
                            TrakHoundAdminPort.Set(instanceConfiguration.AdminHttpPort, instanceConfiguration.HttpPort);
                        }
                        else
                        {
                            _logger.Error($"Admin Port Not Started : Invalid Admin HTTP Address ({adminAddress}). The Admin Interface is served on the HTTP Port");
                        }
                    }
                    else
                    {
                        o.ListenAnyIP(instanceConfiguration.AdminHttpPort);
                        TrakHoundAdminPort.Set(instanceConfiguration.AdminHttpPort, instanceConfiguration.HttpPort);
                    }
                }
            });
        }

        #endregion

        #region "Pipeline"

        /// <summary>
        /// The HTTP Request Pipeline. The order matters : each step only sees Requests the steps before it passed on
        /// </summary>
        private void UseInstancePipeline(WebApplication app)
        {
            InstanceAssets.Initialize(app.Environment.WebRootFileProvider);

            // First, so every other step sees the client's address and scheme instead of the reverse proxy's
            app.UseMiddleware<TrakHoundForwardedHeadersMiddleware>((Func<IEnumerable<string>>)(() => _instance.Configuration?.TrustedProxies));

            // Before anything reads a Request body
            app.UseMiddleware<TrakHoundRequestBodyLimitMiddleware>();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler(new ExceptionHandlerOptions
                {
                    ExceptionHandlingPath = TrakHoundInstanceRoutes.ErrorPrefix,
                    CreateScopeForErrors = true,
                    SuppressDiagnosticsCallback = _ => true // Logged by TrakHoundExceptionLogger (with the Request ID)
                });
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

#if !DEBUG
            app.UseResponseCompression();
#endif

            // Every response, including Package files and the Health endpoints (after the Exception Handler, which clears headers)
            app.UseMiddleware<TrakHoundResponseHeadersMiddleware>();

            app.UseMiddleware<TrakHoundPathBaseMiddleware>();

            // Public Health endpoints. Not recorded in the HTTP Requests, and not affected by Authentication or the System API setting
            app.UseMiddleware<TrakHoundHealthMiddleware>();

            // Record the HTTP Requests (for Diagnostics and HTTP Metrics)
            app.UseMiddleware<TrakHoundHttpConnectionMiddleware>(_instance.HttpMetrics, (Func<HttpContext, string>)(context => TrakHoundInstanceRequests.GetHttpArea(_instance, context)));

            app.UseMiddleware<TrakHoundInitializationPageMiddleware>();
            app.UseMiddleware<TrakHoundErrorPageMiddleware>();
            app.UseMiddleware<TrakHoundRouteValidationMiddleware>();
            app.UseMiddleware<TrakHoundPackageFilesMiddleware>();

            app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(10) });

            app.UseMiddleware<TrakHoundSystemApiSwitchMiddleware>();

            app.UseStaticFiles();
            app.UseRouting();

            app.UseMiddleware<TrakHoundReservedRouteMiddleware>();
            if (TrakHoundAdminPort.IsEnabled) app.UseMiddleware<TrakHoundAdminPortMiddleware>();
            app.UseMiddleware<TrakHoundAdminAddressMiddleware>((Func<TrakHoundInstanceConfiguration>)(() => _instance.Configuration));

            app.UseAuthentication();
            app.UseMiddleware<TrakHoundAdminSessionMiddleware>();
            app.UseAntiforgery();
            app.UseMiddleware<TrakHoundAuthenticationMiddleware>();
            app.UseMiddleware<TrakHoundRateLimitMiddleware>();
            app.UseMiddleware<TrakHoundApiMiddleware>();
        }

        private static void MapEndpoints(WebApplication app)
        {
            // The System API, Sign In, and Admin Session Controllers (attribute routes, so the App Host's catch-all never takes their routes)
            app.MapControllers();
            app.MapTrakHoundMcp();

            // The Admin Interface's Pages and the App Host (Pages/AppHost, the catch-all for App pages). When the Admin Interface is
            // disabled, its Pages are not matched (see AdminInterfaceMatcherPolicy), so those requests go to the App Host
            // The frame-ancestors policy is set by TrakHoundResponseHeadersMiddleware (which also allows the Admin Port's App Preview).
            // Blazor's own 'self' policy would be added as a second header, and browsers apply both, so the App Preview would be blocked
            app.MapRazorComponents<InstanceApp>().AddInteractiveServerRenderMode(o => o.ContentSecurityFrameAncestorsPolicy = null);
        }

        #endregion

        #region "Shutdown"

        private async Task StopInstance()
        {
            _instanceStopped = true;

            try
            {
                await _instance.Stop();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error Stopping Instance");
            }
        }

        // Runs synchronously within the Host's Stopping notification (the Instance itself is Stopped by TrakHoundInstanceShutdownService)
        private void CloseWebSockets()
        {
            if (_webSocketManager != null)
            {
                try
                {
                    // Don't let an unresponsive client hold up shutdown
                    _webSocketManager.DisposeAsync().AsTask().Wait(_webSocketCloseTimeout);
                }
                catch (Exception ex)
                {
                    _logger?.Warn($"Error Closing WebSockets : {ex.Message}");
                }
            }
        }

        #endregion


        /// <summary>
        /// Check if this process is running on Windows in an in process instance in IIS
        /// </summary>
        private static bool IsRunningInProcessIIS()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;

            var processName = Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().ProcessName);
            return processName.Contains("w3wp", StringComparison.OrdinalIgnoreCase) || processName.Contains("iisexpress", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRunningInDocker()
        {
            return Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
        }
    }
}
