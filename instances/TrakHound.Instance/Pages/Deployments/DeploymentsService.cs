// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Deployments;
using TrakHound.Instance.Configurations;
using TrakHound.Instances;

namespace TrakHound.Instance.Pages.Deployments
{
    /// <summary>
    /// Deployment paths and operations for this Instance (one operation at a time)
    /// </summary>
    public class DeploymentsService
    {
        private readonly ITrakHoundInstance _instance;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);


        /// <summary>
        /// The directory containing the Configuration Profile directories (may be set by TRAKHOUND_CONFIGURATION_PATH)
        /// </summary>
        public string ConfigurationPath => Path.GetDirectoryName(_instance.ConfigurationProfile.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        /// <summary>
        /// The Package directory (may be set by TRAKHOUND_PACKAGE_PATH)
        /// </summary>
        public string PackagesPath => _instance.PackageManager.Path;

        /// <summary>
        /// The Volume directory (may be set by TRAKHOUND_VOLUME_PATH)
        /// </summary>
        public string VolumesPath => _instance.VolumesPath;

        /// <summary>
        /// The Package Variables directory (may be set by TRAKHOUND_VARIABLE_PATH)
        /// </summary>
        public string VariablesPath => _instance.VariablesPath;

        /// <summary>
        /// Where the installed Deployment Information file is kept (in the Configuration directory)
        /// </summary>
        public string InformationPath => TrakHoundStartupDeployments.GetInformationPath(ConfigurationPath);

        public string BackupPath => TrakHoundStartupDeployments.BackupPath;

        public string OutputPath => Path.Combine(AppContext.BaseDirectory, "_temp", "deployments");

        public string DeploymentConfigurationPath => Path.Combine(ConfigurationPath, DeploymentConfiguration.Filename);

        public bool IsBusy => _operationLock.CurrentCount == 0;

        public ITrakHoundInstance Instance => _instance;


        public DeploymentsService(ITrakHoundInstance instance)
        {
            _instance = instance;
        }


        public TrakHoundDeploymentInstallOptions CreateInstallOptions(bool backup, IProgress<string> progress = null)
        {
            return new TrakHoundDeploymentInstallOptions
            {
                ConfigurationPath = ConfigurationPath,
                PackagesPath = PackagesPath,
                VolumesPath = VolumesPath,
                VariablesPath = VariablesPath,
                InformationPath = InformationPath,
                BackupPath = backup ? BackupPath : null,
                Progress = progress
            };
        }

        public TrakHoundDeployment GetInstalledDeployment() => TrakHoundDeployment.ReadInformation(InformationPath);

        public IEnumerable<TrakHoundDeploymentBackup> GetBackups() => TrakHoundDeployment.GetBackups(BackupPath);

        public IEnumerable<string> GetProfiles()
        {
            return Directory.Exists(ConfigurationPath)
                ? Directory.GetDirectories(ConfigurationPath).Select(Path.GetFileName).OrderBy(o => o).ToList()
                : new List<string>();
        }

        /// <summary>
        /// The Configuration Categories in the Profiles (all Profiles if none are specified)
        /// </summary>
        public IEnumerable<string> GetCategories(IEnumerable<string> profiles = null) => TrakHoundDeployment.GetCategories(ConfigurationPath, profiles);

        public IEnumerable<TrakHoundDeploymentDataItem> GetVolumes() => TrakHoundDeployment.GetVolumes(VolumesPath);

        public IEnumerable<TrakHoundDeploymentDataItem> GetVariables() => TrakHoundDeployment.GetVariables(VariablesPath);

        /// <summary>
        /// The display name of a Configuration Category (ex. "service" => "Services")
        /// </summary>
        public static string GetCategoryLabel(string category)
        {
            switch (category?.ToLowerInvariant())
            {
                case TrakHoundDeployment.ProfileSettingsCategory: return "Instance Settings";
                case "api": return "Api";
                case "app": return "Apps";
                case "function": return "Functions";
                case "worker": return "Workers";
                case "service": return "Services";
                case "identity": return "Identity Providers";
                case "router": return "Routers";
                case "security": return "Security";
                case "zone": return "Zones";
                case null: return null;
                default: return category.Length > 1 ? char.ToUpperInvariant(category[0]) + category.Substring(1) : category.ToUpperInvariant();
            }
        }

        public DeploymentConfiguration ReadDeploymentConfiguration() => DeploymentConfiguration.Read(DeploymentConfigurationPath) ?? new DeploymentConfiguration();


        /// <summary>
        /// Create a Deployment file (in the background)
        /// </summary>
        public async Task<TrakHoundDeploymentCreateFileResult> Create(TrakHoundDeployment deployment, TrakHoundDeploymentCreateOptions options)
        {
            // A Deployment can include secrets, so creating one is a change
            TrakHound.Instance.Security.TrakHoundAdminContext.DemandWrite(TrakHound.Instance.Security.TrakHoundAdminSecurity.Deployments);

            if (!await _operationLock.WaitAsync(0)) return Busy<TrakHoundDeploymentCreateFileResult>();

            try
            {
                var filename = $"{Sanitize(deployment.ProfileId)}-{Sanitize(deployment.Version)}{TrakHoundDeployment.FileExtension}";
                var outputPath = Path.Combine(OutputPath, Guid.NewGuid().ToString(), filename);

                return await Task.Run(() => deployment.CreateFile(outputPath, options));
            }
            finally
            {
                _operationLock.Release();
            }
        }

        /// <summary>
        /// Run an operation that creates several Deployment files (ex. one for each Device), so other Deployment operations wait for it.
        /// Returns null if another operation is running
        /// </summary>
        public async Task<T> RunExclusive<T>(Func<Task<T>> operation) where T : class
        {
            TrakHound.Instance.Security.TrakHoundAdminContext.DemandWrite(TrakHound.Instance.Security.TrakHoundAdminSecurity.Deployments);

            if (!await _operationLock.WaitAsync(0)) return null;

            try
            {
                return await operation();
            }
            finally
            {
                _operationLock.Release();
            }
        }

        /// <summary>
        /// Stop the Instance, install the Deployment, and start the Instance again (also if the Install fails)
        /// </summary>
        public Task<DeploymentOperationResult> Install(byte[] deploymentBytes, bool backup, IProgress<string> progress)
        {
            TrakHound.Instance.Security.TrakHoundAdminContext.DemandWrite(TrakHound.Instance.Security.TrakHoundAdminSecurity.Deployments);
            return RunWithInstanceStopped(progress, () => TrakHoundDeployment.Install(deploymentBytes, CreateInstallOptions(backup, progress)));
        }

        /// <summary>
        /// Stop the Instance, restore the Backup, and start the Instance again
        /// </summary>
        public Task<DeploymentOperationResult> Restore(string backupPath, IProgress<string> progress)
        {
            TrakHound.Instance.Security.TrakHoundAdminContext.DemandWrite(TrakHound.Instance.Security.TrakHoundAdminSecurity.Deployments);
            return RunWithInstanceStopped(progress, () => TrakHoundDeployment.Restore(backupPath, CreateInstallOptions(false, progress)));
        }

        private async Task<DeploymentOperationResult> RunWithInstanceStopped(IProgress<string> progress, Func<TrakHoundInstallDeploymentResult> operation)
        {
            var result = new DeploymentOperationResult();

            if (!await _operationLock.WaitAsync(0))
            {
                result.Errors.Add("Another deployment operation is running");
                return result;
            }

            try
            {
                progress?.Report("Stopping Instance..");
                await _instance.Stop();

                var operationResult = await Task.Run(operation);
                result.Success = operationResult.Success;
                result.BackupPath = operationResult.BackupPath;
                if (operationResult.Messages != null) result.Messages.AddRange(operationResult.Messages);
                if (operationResult.Errors != null) result.Errors.AddRange(operationResult.Errors);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Errors.Add(ex.Message);
            }
            finally
            {
                // Always start the Instance again (a failed Install must not leave it stopped)
                try
                {
                    progress?.Report("Starting Instance..");
                    await _instance.Start();
                    result.Restarted = _instance.Status == TrakHoundInstanceStatus.Started;
                    if (!result.Restarted) result.Errors.Add($"The Instance did not start (status : {_instance.Status}). Check the Instance log.");
                }
                catch (Exception ex)
                {
                    result.Restarted = false;
                    result.Errors.Add($"The Instance could not be started : {ex.Message}");
                }

                _operationLock.Release();
            }

            return result;
        }

        private static T Busy<T>() where T : TrakHoundDeploymentCreateFileResult, new()
        {
            var result = new T();
            result.Errors.Add("Another deployment operation is running");
            return result;
        }

        private static string Sanitize(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string((value ?? "").Select(c => invalid.Contains(c) || c == ' ' ? '-' : c).ToArray()).Trim('-');
            return string.IsNullOrEmpty(sanitized) ? "deployment" : sanitized;
        }
    }

    public class DeploymentOperationResult
    {
        public bool Success { get; set; }

        /// <summary>
        /// True if the Instance was started again after the operation
        /// </summary>
        public bool Restarted { get; set; }

        public string BackupPath { get; set; }

        public List<string> Messages { get; set; } = new List<string>();

        public List<string> Errors { get; set; } = new List<string>();
    }
}
