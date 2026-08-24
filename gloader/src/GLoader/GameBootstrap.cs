using System;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace GLoader
{
    internal static class GameBootstrap
    {
        public static Assembly Load(string targetPath)
        {
            Log.Info("Loading managed Terraria assembly.");
            return Assembly.LoadFrom(targetPath);
        }

        public static int InvokeEntryPoint(Assembly gameAssembly, string[] gameArguments)
        {
            var entryPoint = gameAssembly.EntryPoint;
            if (entryPoint == null)
            {
                throw new MissingMethodException(gameAssembly.FullName, "<entry point>");
            }

            var parameters = entryPoint.GetParameters();
            object[] invokeArguments;

            if (parameters.Length == 0)
            {
                invokeArguments = null;
            }
            else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string[]))
            {
                invokeArguments = new object[] { gameArguments };
            }
            else
            {
                throw new NotSupportedException(
                    "Unsupported Terraria entry point signature: " + entryPoint);
            }

            try
            {
                var result = entryPoint.Invoke(null, invokeArguments);
                return result is int exitCode ? exitCode : 0;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }
}
