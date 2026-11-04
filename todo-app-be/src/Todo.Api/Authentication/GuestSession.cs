using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Todo.Api.Endpoints;
using Todo.Application.Security;

namespace Todo.Api.Authentication;

public sealed class GuestSession(IDataProtectionProvider protection, IWebHostEnvironment environment,
    UserManager<IdentityUser<Guid>> users, SignInManager<IdentityUser<Guid>> signIn, IGuestData data)
{
    private const string CookieName = "Todo.GuestDraft";
    private readonly ITimeLimitedDataProtector protector = protection.CreateProtector("Todo.GuestDraft.v1").ToTimeLimitedDataProtector();

    public async Task<IdentityUser<Guid>> StartAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasClaim(GuestIdentity.ClaimType, "true"))
            throw new AccessDeniedException();
        IdentityUser<Guid>? guest = null;
        var id = Read(context);
        if (id.HasValue) guest = await users.FindByIdAsync(id.Value.ToString());
        if (guest is not null && !await IsGuestAsync(guest)) guest = null;
        if (guest is null && context.User.HasClaim(GuestIdentity.ClaimType, "true"))
            guest = await users.GetUserAsync(context.User);
        if (guest is null)
        {
            var guestId = Guid.NewGuid();
            guest = new IdentityUser<Guid> { Id = guestId, UserName = $"guest_{guestId:N}", Email = $"{guestId:N}@guest.todo.invalid" };
            if (!(await users.CreateAsync(guest)).Succeeded) throw new InvalidOperationException("Guest session could not be created.");
            if (!(await users.AddClaimAsync(guest, new Claim(GuestIdentity.ClaimType, "true"))).Succeeded)
            {
                await users.DeleteAsync(guest);
                throw new InvalidOperationException("Guest session could not be initialized.");
            }
        }
        await signIn.SignInAsync(guest, isPersistent: false);
        context.Response.Cookies.Append(CookieName, protector.Protect(guest.Id.ToString(), TimeSpan.FromHours(8)), new CookieOptions
        {
            HttpOnly = true, Secure = !environment.IsDevelopment() || context.Request.IsHttps,
            SameSite = SameSiteMode.Lax, Path = "/", MaxAge = TimeSpan.FromHours(8), IsEssential = true
        });
        return guest;
    }

    public async Task<AccountResponse> AccountAsync(IdentityUser<Guid> user, HttpContext context, CancellationToken cancellationToken)
    {
        var guest = await IsGuestAsync(user);
        var source = Read(context);
        var summary = !guest && source.HasValue && source.Value != user.Id
            ? await data.SummaryAsync(source.Value, cancellationToken) : new GuestDataSummary(0, 0);
        return new(user.Id, guest ? string.Empty : user.Email!, guest, summary.Tasks > 0 || summary.Projects > 0);
    }

    public async Task<GuestDataSummary> SyncAsync(HttpContext context, Guid accountId, CancellationToken cancellationToken)
    {
        var source = Read(context) ?? throw new AccessDeniedException();
        var result = await data.MergeAsync(source, accountId, cancellationToken);
        context.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/" });
        return result;
    }

    private async Task<bool> IsGuestAsync(IdentityUser<Guid> user) =>
        (await users.GetClaimsAsync(user)).Any(claim => claim.Type == GuestIdentity.ClaimType && claim.Value == "true");

    private Guid? Read(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var value)) return null;
        try { return Guid.TryParse(protector.Unprotect(value, out _), out var id) ? id : null; }
        catch (CryptographicException) { return null; }
    }
}