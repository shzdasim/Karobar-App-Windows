using Karobar.Application.Interfaces;
using Karobar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karobar.Infrastructure.Security;

public sealed class AuthService : IAuthService
{
    private readonly IDbContextFactory<KarobarDbContext> _dbFactory;

    public AuthenticatedUserSession? CurrentSession { get; private set; }

    public AuthService(IDbContextFactory<KarobarDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<AuthenticatedUserSession> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Please enter both username and password.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var normalizedUsername = username.Trim().ToLowerInvariant();

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username.ToLower() == normalizedUsername, cancellationToken);

        if (user == null || !VerifyBcryptHash(password, user.Password))
            throw new InvalidOperationException("Invalid username or password.");

        if (!string.IsNullOrWhiteSpace(user.Status) &&
            !user.Status.Equals("active", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Your account is inactive. Please contact the administrator.");
        }

        var roleIds = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.ModelId == user.Id)
            .Select(ur => ur.RoleId)
            .ToListAsync(cancellationToken);

        var roleNames = await db.Roles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Name)
            .ToListAsync(cancellationToken);

        var rolePermIds = await db.RolePermissions.AsNoTracking()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.PermissionId)
            .ToListAsync(cancellationToken);

        var directPermIds = await db.UserPermissions.AsNoTracking()
            .Where(up => up.ModelId == user.Id)
            .Select(up => up.PermissionId)
            .ToListAsync(cancellationToken);

        var allPermIds = rolePermIds.Concat(directPermIds).Distinct().ToList();
        var permNames = await db.Permissions.AsNoTracking()
            .Where(p => allPermIds.Contains(p.Id))
            .Select(p => p.Name)
            .ToListAsync(cancellationToken);

        var session = new AuthenticatedUserSession
        {
            UserId = user.Id,
            Username = user.Username,
            Name = user.Name,
            Email = user.Email,
            Roles = roleNames,
            Permissions = new HashSet<string>(permNames, StringComparer.OrdinalIgnoreCase)
        };

        CurrentSession = session;
        return session;
    }

    public async Task<bool> VerifyPasswordAsync(long userId, string password, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
            return false;

        return VerifyBcryptHash(password, user.Password);
    }

    public void Logout()
    {
        CurrentSession = null;
    }

    /// <summary>
    /// Supports both PHP/Laravel $2y$ BCrypt hashes and .NET $2a$/$2b$ BCrypt hashes.
    /// </summary>
    public static bool VerifyBcryptHash(string plainPassword, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash))
            return false;

        try
        {
            var normalizedHash = storedHash.StartsWith("$2y$", StringComparison.Ordinal)
                ? "$2a$" + storedHash[4..]
                : storedHash;

            return BCrypt.Net.BCrypt.Verify(plainPassword, normalizedHash);
        }
        catch
        {
            return false;
        }
    }
}
