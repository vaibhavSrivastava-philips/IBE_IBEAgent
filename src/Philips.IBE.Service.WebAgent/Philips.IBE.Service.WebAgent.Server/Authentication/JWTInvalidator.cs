using System.Collections.Concurrent;

namespace Philips.IBE.Service.WebAgent.Server.Authentication
{
    public class JWTInvalidator
    {
        private readonly ConcurrentDictionary<string, DateTime> _blacklistedTokens = new ConcurrentDictionary<string, DateTime>();

        public virtual void AddToken(string token, DateTime expiry)
        {
            _blacklistedTokens[token] = expiry;
        }

        public bool IsTokenBlacklisted(string token)
        {
            CleanUpExpiredTokens();
            return _blacklistedTokens.ContainsKey(token);
        }

        private void CleanUpExpiredTokens()
        {
            var now = DateTime.UtcNow;
            var expiredTokens = _blacklistedTokens.Where(t => t.Value <= now).Select(t => t.Key).ToList();
            foreach (var token in expiredTokens)
            {
                _blacklistedTokens.TryRemove(token, out _);
            }
        }
    }
}
