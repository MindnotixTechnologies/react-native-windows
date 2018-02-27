using Newtonsoft.Json.Linq;
using ReactNative.Bridge;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace ReactNative
{
    /// <summary>
    /// Base implementation for <see cref="IReactDelegateFactory"/>.
    /// </summary>
    public abstract class ReactDelegateFactoryBase : IReactDelegateFactory
    {
        /// <summary>
        /// Discriminator for asynchronous methods.
        /// </summary>
        public const string AsyncMethodType = "async";

        /// <summary>
        /// Discriminator for synchronous methods.
        /// </summary>
        public const string SyncMethodType = "sync";

        /// <summary>
        /// Discriminator for methods with promises.
        /// </summary>
        public const string PromiseMethodType = "promise";
        
        /// <summary>
        /// Instantiates a <see cref="ReactDelegateFactoryBase"/>.
        /// </summary>
        protected ReactDelegateFactoryBase() { }

        /// <summary>
        /// Create an invocation delegate from the given method.
        /// </summary>
        /// <param name="nativeModule">The native module instance.</param>
        /// <param name="method">The method.</param>
        /// <returns>The invocation delegate.</returns>
        public abstract Func<INativeModule, IReactInstance, JArray, JToken> Create(INativeModule nativeModule,
            MethodInfo method);

        /// <summary>
        /// Extracts the native method type from the method.
        /// </summary>
        /// <param name="method">The method.</param>
        /// <returns>The native method type.</returns>
        public string GetMethodType(MethodInfo method)
        {
            var attribute = method.GetCustomAttribute<ReactMethodAttribute>();
            if (attribute != null && attribute.IsBlockingSynchronousMethod)
            {
                return SyncMethodType;
            }

            if (method.ReturnType == typeof(Task))
            {
                throw new NotImplementedException("Async methods are not yet supported.");
            }

            var parameters = method.GetParameters();
            if (parameters.Length > 0 && parameters.Last().ParameterType == typeof(IPromise))
            {
                return PromiseMethodType;
            }

            return AsyncMethodType;
        }

        /// <summary>
        /// Check that the method is valid for <see cref="ReactMethodAttribute"/>.
        /// </summary>
        /// <param name="method">The method.</param>
        public void Validate(MethodInfo method)
        {
            var attribute = method.GetCustomAttribute<ReactMethodAttribute>();
            if (attribute != null && attribute.IsBlockingSynchronousMethod)
            {
                ValidateDirectMethod(method);
                return;
            }

            if (attribute != null && attribute.IsBlockingSynchronousMethod)
            {
                // We don't have any method type assumption about SyncHook.
                return;
            }
            var returnType = method.ReturnType;
            if (returnType != typeof(Task) && returnType != typeof(void))
            {
                throw new NotSupportedException("Native module methods must either return void or Task.");
            }

            var parameters = method.GetParameters();
            var parameterCount = parameters.Length;
            for (var i = 0; i < parameterCount; ++i)
            {
                var parameterType = parameters[i].ParameterType;
                if (parameterType == typeof(IPromise) && i != (parameterCount - 1))
                {
                    throw new NotSupportedException("Promises are only supported as the last parameter of a native module method.");
                }
                else if (parameterType == typeof(ICallback) && i != (parameterCount - 1))
                {
                    if (i != (parameterCount - 2) || parameters[parameterCount - 1].ParameterType != typeof(ICallback))
                    {
                        throw new NotSupportedException("Callbacks are only supported in the last two positions of a native module method.");
                    }
                }
                else if (returnType == typeof(Task) && (parameterType == typeof(ICallback) || parameterType == typeof(IPromise)))
                {
                    throw new NotSupportedException("Callbacks and promises are not supported in async native module methods.");
                }
            }
        }

        private static void ValidateDirectMethod(MethodInfo method)
        {
            if (method.ReturnType != typeof(JToken))
            {
                throw new NotSupportedException("Direct-call methods must return JToken.");
            }

            var parameters = method.GetParameters();
            var parameterCount = parameters.Length;
            if (parameterCount != 3)
            {
                throw new NotSupportedException("Direct-call methods must take three parameters.");
            }

            if (parameters[0].ParameterType != typeof(INativeModule))
            {
                throw new NotSupportedException("Direct-call methods must take an INativeModule for the first parameter.");
            }

            if (parameters[1].ParameterType != typeof(IReactInstance))
            {
                throw new NotSupportedException("Direct-call methods must take an IReactInstance for the second parameter.");
            }

            if (parameters[2].ParameterType != typeof(JArray))
            {
                throw new NotSupportedException("Direct-call methods must take a JArray for the third parameter.");
            }
        }

        /// <summary>
        /// Create a callback.
        /// </summary>
        /// <param name="callbackToken">The callback ID token.</param>
        /// <param name="reactInstance">The React instance.</param>
        /// <returns>The callback.</returns>
        protected static ICallback CreateCallback(JToken callbackToken, IReactInstance reactInstance)
        {
            var id = callbackToken.Value<int>();
            return new Callback(id, reactInstance);
        }

        /// <summary>
        /// Create a promise.
        /// </summary>
        /// <param name="resolveToken">The resolve callback ID token.</param>
        /// <param name="rejectToken">The reject callback ID token.</param>
        /// <param name="reactInstance">The React instance.</param>
        /// <returns>The promise.</returns>
        protected static IPromise CreatePromise(JToken resolveToken, JToken rejectToken, IReactInstance reactInstance)
        {
            var resolveCallback = CreateCallback(resolveToken, reactInstance);
            var rejectCallback = CreateCallback(rejectToken, reactInstance);
            return new Promise(resolveCallback, rejectCallback);
        }

        class Callback : ICallback
        {
            private static readonly object[] s_empty = new object[0];

            private readonly int _id;
            private readonly IReactInstance _instance;

            public Callback(int id, IReactInstance instance)
            {
                _id = id;
                _instance = instance;
            }

            public void Invoke(params object[] arguments)
            {
                _instance.InvokeCallback(_id, JArray.FromObject(arguments ?? s_empty));
            }
        }
    }
}
