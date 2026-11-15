// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Reflection;

namespace TrakHound.Instance.Runtime.Tests
{
    /// <summary>
    /// Creates a stand-in for an interface whose members return defaults (an empty Task for async members) unless a handler is set by name,
    /// so the Managers can be created without a running Instance
    /// </summary>
    public class Fake : DispatchProxy
    {
        private readonly Dictionary<string, Func<object[], object>> _handlers = new Dictionary<string, Func<object[], object>>();


        public static T Create<T>(Action<Fake> setup = null) where T : class
        {
            var proxy = DispatchProxy.Create<T, Fake>();
            setup?.Invoke((Fake)(object)proxy);
            return proxy;
        }

        public Fake On(string memberName, Func<object[], object> handler)
        {
            _handlers[memberName] = handler;
            return this;
        }


        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (_handlers.TryGetValue(targetMethod.Name, out var handler)) return handler(args);

            var returnType = targetMethod.ReturnType;
            if (returnType == typeof(void)) return null;
            if (returnType == typeof(Task)) return Task.CompletedTask;
            if (returnType == typeof(ValueTask)) return default(ValueTask);
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = returnType.GetGenericArguments()[0];
                var result = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
                return typeof(Task).GetMethod(nameof(Task.FromResult)).MakeGenericMethod(resultType).Invoke(null, new[] { result });
            }

            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }
}
