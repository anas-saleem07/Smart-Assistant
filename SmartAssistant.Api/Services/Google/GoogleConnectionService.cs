using Microsoft.EntityFrameworkCore;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Models;

namespace SmartAssistant.Api.Services.Google
{
    public interface IGoogleConnectionService
    {
        Task<GoogleConnectionStatusModel> GetStatusAsync(CancellationToken ct);
        Task<bool> DisconnectAsync(CancellationToken ct);
    }
    public sealed class GoogleConnectionService : IGoogleConnectionService
    {
        private readonly ApplicationDbContext _db;
        private readonly IOAuthTokenHelper _oauthTokenHelper;

        public GoogleConnectionService(
            ApplicationDbContext db,
            IOAuthTokenHelper oauthTokenHelper)
        {
            _db = db;
            _oauthTokenHelper = oauthTokenHelper;
        }

        public async Task<GoogleConnectionStatusModel> GetStatusAsync(CancellationToken ct)
        {
            var account = await _oauthTokenHelper.GetLatestActiveGmailAccountAsync(ct);

            if (account == null)
            {
                return new GoogleConnectionStatusModel
                {
                    IsConnected = false,
                    NeedsReconnect = false,
                    Provider = "Gmail",
                    Email = "",
                    Message = "Gmail is not connected. Please login to continue."
                };
            }

            if (account.NeedsReconnect)
            {
                return new GoogleConnectionStatusModel
                {
                    IsConnected = false,
                    NeedsReconnect = true,
                    Provider = "Gmail",
                    Email = account.Email ?? "",
                    Message = "Gmail needs reconnect. Please login again."
                };
            }

            return new GoogleConnectionStatusModel
            {
                IsConnected = true,
                NeedsReconnect = false,
                Provider = "Gmail",
                Email = account.Email ?? "",
                Message = "Gmail is connected."
            };
        }

        public async Task<bool> DisconnectAsync(CancellationToken ct)
        {
            var accounts = await _db.EmailOAuthAccounts
                .Where(account => account.Active && account.Provider == "Gmail")
                .ToListAsync(ct);

            if (accounts.Count == 0)
                return true;

            foreach (var account in accounts)
            {
                account.Active = false;
                account.UpdatedOn = DateTimeOffset.UtcNow;
            }

            await _db.SaveChangesAsync(ct);
            return true;
        }
    }
}