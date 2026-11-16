# TrakHound.Oidc.Identity

An Identity Provider that signs users in with an OpenID Connect server (ex. Microsoft Entra ID, Okta or Keycloak) and accepts that server's JWT access tokens from API clients. Use it for single sign on, MFA, or when your users and their groups are managed in an identity server.

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.Oidc.Identity` |
| Category | `identity` |

## How it signs users in
**Browsers** use the Authorization Code flow with PKCE:

1. A user opens a protected App or page, or `/_identity/signin/{providerId}`, and is redirected to the server's sign in page.
2. The server redirects back to the Instance's Redirect URI, `/_identity/callback/{providerId}`.
3. The provider exchanges the code for tokens and validates the ID token (signature, issuer, audience, lifetime and nonce).
4. The Instance starts its own session (a cookie). The session lasts for the Instance's session lifetime, not the ID token's, which is usually short.

A sign in must be finished within 15 minutes, and each one can only be completed once. Signing out of the Instance (`/_identity/signout`) also signs out of the server when its metadata has an end session endpoint.

**API clients** send the server's access token as `Authorization: Bearer <token>`. The token must be a JWT from this server's issuer with an audience in `audience`, and is validated on its signature, issuer, audience and lifetime. A token issued to an application without a user (ex. the Client Credentials flow) is identified by its `azp` or `client_id` claim. A validated token is reused for up to 5 minutes (never past its expiration), so it isn't validated again on every request. Bearer tokens that aren't JWTs, or are from another issuer, are left for the Instance's other Identity Providers (ex. API Keys).

The server's metadata is read from `{authority}/.well-known/openid-configuration` and refreshed automatically, including when the server rotates its signing keys.

The signed in identity has:

| Property | Value |
|---|---|
| User ID | The `userIdClaim` claim |
| Name | The `nameClaim` claim, else `preferred_username`, else `email` |
| Roles | The values of every claim in `rolesClaim` |
| Properties | `email` and `username` (`preferred_username`), when the token has them |

Grant those Roles permissions on your Apps and Apis with a Security Profile that uses this provider (Config > Access > Security Profiles).

## Configuration
1. Add the provider on Security > Providers > **Add Identity Provider**, choose this package and set its Parameters.
2. Security > Providers shows the provider's **Redirect URI** (`https://<instance>/_identity/callback/<providerId>`). Register it with your server as a redirect URI for the client.
3. To return users to the Instance after they sign out of the server, also register the page they return to (ex. `https://<instance>/`) as a post logout redirect URI. The Instance sends the sign out's `returnUrl` on the Instance's own address.

| Parameter | Required | Default | Description |
|---|---|---|---|
| `authority` | Yes | | The issuer URL (ex. `https://login.microsoftonline.com/<tenant-id>/v2.0` or `https://keycloak.example.com/realms/<realm>`) |
| `clientId` | Yes | | The client ID registered with the server |
| `clientSecret` | No | | The client secret. Leave it out for a public client, which uses PKCE only |
| `scopes` | No | `openid profile email` | Space or comma separated scopes. `openid` is always added |
| `rolesClaim` | No | `roles,groups` | Comma separated claims to read Roles from. Use dots for nested claims (ex. `realm_access.roles` for Keycloak) |
| `userIdClaim` | No | `sub` | The claim for the User ID |
| `nameClaim` | No | `name` | The claim for the display name |
| `audience` | No | The `clientId` | Comma separated audiences accepted for Bearer tokens. ID tokens must always have the `clientId` as their audience |
| `requireHttpsMetadata` | No | `true` | Require HTTPS for the server's metadata. Set it to `false` only for a development server |

Example Parameters for Keycloak:

```yaml
authority: https://keycloak.example.com/realms/factory
clientId: trakhound
clientSecret: <client-secret>
rolesClaim: realm_access.roles
```

## Security
- Parameters whose names contain `secret` (ex. `clientSecret`) are encrypted when the configuration is saved.
- Keep `requireHttpsMetadata` on outside of development, so the signing keys can't be read from (or replaced on) an unencrypted connection.
- Tokens are checked against a clock skew of 2 minutes.
- A Bearer token stays valid until it expires, since a JWT can't be revoked before then. Keep access token lifetimes short on the server.
- If the server can't be reached when the metadata is first read, the provider waits 30 seconds before trying again, and sign ins fail until then.

## Development
Test the configuration on Security > Providers with **Test Sign In**, which opens the sign in in a new tab. Sign in problems are written to the `security` log.

## Documentation
- Identity: https://www.trakhound.com/docs/trakhound-framework/identity
- Security: https://www.trakhound.com/docs/trakhound-framework/security
