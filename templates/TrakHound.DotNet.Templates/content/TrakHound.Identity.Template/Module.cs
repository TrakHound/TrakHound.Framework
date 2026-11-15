using System.Threading.Tasks;
using TrakHound.Clients;
using TrakHound.Security;
using TrakHound.Variables;
using TrakHound.Volumes;

public class Module : TrakHoundIdentityProviderBase
{
    public Module(ITrakHoundIdentityProviderConfiguration configuration, ITrakHoundClient client, ITrakHoundVolume volume, ITrakHoundVariableCollection variables) : base(configuration, client, volume, variables) { }


    // Token: verify credentials sent with each Request (return NoResult if the Request has none for this Identity Provider)
    // Redirect: override OnChallenge and OnHandleCallback (ex. OpenID Connect)
    // Credentials: override OnSignIn (Username and Password from the Instance's sign in page)
    public override TrakHoundIdentityProviderFeatures Features => TrakHoundIdentityProviderFeatures.Token;


    protected override Task<TrakHoundIdentityResult> OnAuthenticate(TrakHoundIdentityContext context)
    {
        // Use a Header unique to this Identity Provider (other Identity Providers may also read the Authorization header)
        var token = context.Request.GetHeader("X-Example-Token");
        if (string.IsNullOrEmpty(token)) return Task.FromResult(TrakHoundIdentityResult.NoResult());

        // Verify the Token and return the Identity (Roles are mapped to Permissions by Security Profile Assignments)
        //var identity = new TrakHoundIdentity();
        //identity.UserId = "user";
        //identity.Roles = new[] { "operators" };
        //return Task.FromResult(TrakHoundIdentityResult.Success(identity));

        return Task.FromResult(TrakHoundIdentityResult.Fail("Not Implemented"));
    }
}
