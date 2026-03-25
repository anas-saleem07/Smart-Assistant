using System;

namespace SmartAssistant.Api.Services.Google
{
    public sealed class GoogleOAuthReconnectRequiredException : InvalidOperationException
    {
        public GoogleOAuthReconnectRequiredException(string message)
            : base(message)
        {
        }

        public GoogleOAuthReconnectRequiredException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}