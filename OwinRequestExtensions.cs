using System.Net.Http;

// ReSharper disable once CheckNamespace
namespace NuGet.Modules
{
    public static class OwinRequestExtensions
    {
        public static string GetIpAddress(this HttpRequestMessage request)
        {
            string ipAddress = null;
            var owinContext = request.GetOwinContext();
            if (owinContext != null)
            {
                ipAddress = request.GetOwinContext().Request.RemoteIpAddress;
            }
            return ipAddress;
        }
    }
}