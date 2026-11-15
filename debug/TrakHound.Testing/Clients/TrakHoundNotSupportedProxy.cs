// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Reflection;

namespace TrakHound.Testing.Clients
{
    /// <summary>
    /// Implements a Client interface that the test Client doesn't fake: every call throws a NotSupportedException that names the
    /// property to set, so a test fails clearly instead of getting a null response
    /// </summary>
    public class TrakHoundNotSupportedProxy : DispatchProxy
    {
        private string _name;


        public static T Create<T>(string name) where T : class
        {
            var proxy = Create<T, TrakHoundNotSupportedProxy>();
            ((TrakHoundNotSupportedProxy)(object)proxy)._name = name;
            return proxy;
        }

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            throw new NotSupportedException($"TrakHoundTestClient doesn't fake Client.{_name}.{targetMethod?.Name}. Set TrakHoundTestClient.{_name} to your own implementation (ex. a mock)");
        }
    }
}
