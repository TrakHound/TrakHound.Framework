// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Reflection;
using System.Runtime.ExceptionServices;
using TrakHound.Api;
using TrakHound.Clients;
using TrakHound.Functions;
using TrakHound.Logging;
using TrakHound.Security;
using TrakHound.Services;
using TrakHound.Variables;
using TrakHound.Volumes;
using TrakHound.Workers;

namespace TrakHound.Hosting
{
    /// <summary>
    /// Creates modules the same way the Instance does: the constructor the Instance calls first, then the Client, Volume and Variables.
    /// Used by the hosts outside of an Instance, the debug host (TrakHoundDebugHost) and tests (TrakHound.Testing)
    /// </summary>
    public static class TrakHoundModuleFactory
    {
        /// <summary>
        /// Creates a Function with its (configuration) constructor (or the older (configuration, client, volume) one)
        /// </summary>
        public static TFunction CreateFunction<TFunction>(ITrakHoundFunctionConfiguration configuration, ITrakHoundClient client, ITrakHoundVolume volume, ITrakHoundVariableCollection variables, string instanceId = null) where TFunction : TrakHoundFunction
        {
            var function = CreateModule<TFunction>(
                (new[] { typeof(ITrakHoundFunctionConfiguration) }, new object[] { configuration }),
                (new[] { typeof(ITrakHoundFunctionConfiguration), typeof(ITrakHoundClient), typeof(ITrakHoundVolume) }, new object[] { configuration, client, volume })
                );

            function.InstanceId = instanceId;
            function.SetClient(client);
            function.SetVolume(volume);
            function.SetVariables(variables);
            return function;
        }

        /// <summary>
        /// Creates a Worker with its (configuration) constructor (or the older (configuration, client, volume) one)
        /// </summary>
        public static TWorker CreateWorker<TWorker>(ITrakHoundWorkerConfiguration configuration, ITrakHoundClient client, ITrakHoundVolume volume, ITrakHoundVariableCollection variables, string instanceId = null) where TWorker : TrakHoundWorker
        {
            var worker = CreateModule<TWorker>(
                (new[] { typeof(ITrakHoundWorkerConfiguration) }, new object[] { configuration }),
                (new[] { typeof(ITrakHoundWorkerConfiguration), typeof(ITrakHoundClient), typeof(ITrakHoundVolume) }, new object[] { configuration, client, volume })
                );

            worker.InstanceId = instanceId;
            worker.SetClient(client);
            worker.SetVolume(volume);
            worker.SetVariables(variables);
            return worker;
        }

        /// <summary>
        /// Creates a Service with its (configuration) constructor. Services get no Client
        /// </summary>
        public static TService CreateService<TService>(ITrakHoundServiceConfiguration configuration, ITrakHoundVolume volume, ITrakHoundVariableCollection variables, string instanceId = null) where TService : TrakHoundService
        {
            var service = CreateModule<TService>(
                (new[] { typeof(ITrakHoundServiceConfiguration) }, new object[] { configuration })
                );

            service.InstanceId = instanceId;
            service.SetVolume(volume);
            service.SetVariables(variables);
            return service;
        }

        /// <summary>
        /// Creates an Identity Provider with its (configuration, client, volume, variables) constructor
        /// </summary>
        public static TProvider CreateIdentityProvider<TProvider>(ITrakHoundIdentityProviderConfiguration configuration, ITrakHoundClient client, ITrakHoundVolume volume, ITrakHoundVariableCollection variables) where TProvider : ITrakHoundIdentityProvider
        {
            return CreateModule<TProvider>(
                (new[] { typeof(ITrakHoundIdentityProviderConfiguration), typeof(ITrakHoundClient), typeof(ITrakHoundVolume), typeof(ITrakHoundVariableCollection) }, new object[] { configuration, client, volume, variables })
                );
        }

        /// <summary>
        /// Creates an Api Controller with its parameterless constructor, sets its members, and calls Initialize and InitializeAsync
        /// (an exception from them is thrown, where the Instance logs it and still loads the Controller)
        /// </summary>
        public static async Task<TController> CreateApiController<TController>(ITrakHoundApiConfiguration configuration, ITrakHoundClient client, ITrakHoundVolume volume, ITrakHoundVariableCollection variables, ITrakHoundLogger logger, string instanceId = null) where TController : TrakHoundApiController
        {
            var controller = CreateModule<TController>((Type.EmptyTypes, Array.Empty<object>()));

            controller.InstanceId = instanceId;
            controller.SetConfiguration(configuration);
            controller.SetClient(client);
            controller.SetVolume(volume);
            controller.SetVariables(variables);
            controller.SetLogger(logger);

            controller.Initialize();
            await controller.InitializeAsync();
            return controller;
        }

        /// <summary>
        /// Applies the FunctionParameter defaults of the Function type to the input, the same as the Instance does before a Run.
        /// Returns the reason the Instance would refuse the Run (it returns 400), or null if the input is valid
        /// </summary>
        public static string PrepareFunctionParameters(Type functionType, IReadOnlyDictionary<string, string> input, out IReadOnlyDictionary<string, string> parameters)
        {
            parameters = input ?? new Dictionary<string, string>();

            var definitions = TrakHoundFunctionParameterInformation.Create(functionType);
            if (definitions == null || definitions.Count < 1) return null;

            parameters = TrakHoundFunctionParameterInformation.ApplyDefaults(definitions, parameters);
            return TrakHoundFunctionParameterInformation.Validate(definitions, parameters);
        }


        /// <summary>
        /// Creates the module with the first constructor found (in the order the Instance tries them)
        /// </summary>
        private static TModule CreateModule<TModule>(params (Type[] Types, object[] Arguments)[] constructors)
        {
            foreach (var constructor in constructors)
            {
                var constructorInfo = typeof(TModule).GetConstructor(constructor.Types);
                if (constructorInfo != null)
                {
                    try
                    {
                        return (TModule)constructorInfo.Invoke(constructor.Arguments);
                    }
                    catch (TargetInvocationException ex) when (ex.InnerException != null)
                    {
                        // Show the module's own exception instead of the reflection wrapper
                        ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    }
                }
            }

            var expected = string.Join(" or ", constructors.Select(o => $"({string.Join(", ", o.Types.Select(t => t.Name))})"));
            throw new InvalidOperationException($"{typeof(TModule).Name} needs a public constructor the Instance can call : {expected}");
        }
    }
}
