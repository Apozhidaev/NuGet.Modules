using System;

// ReSharper disable once CheckNamespace
namespace NuGet.Modules.Redirect
{
    public class ProcessRequestEventArgs : EventArgs
    {
        public ProcessRequestEventArgs(Exception exception)
        {
            Exception = exception;
        }

        public Exception Exception { get; }
    }
}