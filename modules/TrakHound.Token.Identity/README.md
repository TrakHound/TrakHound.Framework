# TrakHound.Token.Identity

An Identity Provider that authenticates requests with one static token set in its configuration. Every request with that token is signed in as the same user, with the same Roles. Use it for a script, device or other machine-to-machine client on a trusted network, and for development. For people, use an OpenID Connect or LDAP provider. For several clients, use the Instance's API Keys, which can each be revoked and can expire.

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.Token.Identity` |
| Category | `identity` |

## How it authenticates
Clients send the token on each request in a `token` header (or a `token` query parameter):

```
curl -H "token: <token>" https://<instance>/<api-route>
```

- A request with no `token` is left for the Instance's other Identity Providers.
- A request whose `token` matches the configured `token` is signed in with the configured `userId` and `roles`.
- A request whose `token` doesn't match is refused (`401`) when the resource requires signing in. Public resources still work.

There's no sign in page or session: the token is checked on every request.

Grant the configured Roles permissions on your Apps and Apis with a Security Profile that uses this provider (Config > Access > Security Profiles).

## Configuration
Add the provider on Security > Providers > **Add Identity Provider**, choose this package and set its Parameters.

| Parameter | Required | Default | Description |
|---|---|---|---|
| `token` | Yes | | The token clients must send. Without it, every request with a `token` is refused |
| `userId` | No | The provider's ID | The User ID that requests with the token are signed in as. It's shown in the logs and the Audit Log |
| `roles` | No | | Comma separated Roles (ex. `operators,engineers`) |

Example Parameters:

```yaml
token: <long-random-token>
userId: line3-gateway
roles: operators
```

## Security
- Use a long random token (ex. 32 random bytes, base64 encoded). Anyone with the token has its Roles.
- Parameters whose names contain `token` are encrypted when the configuration is saved.
- Send the token in the `token` header, not the query parameter. The Instance hides `token` query values in its Recent Requests, but proxies and browsers may still log URLs.
- Serve the Instance over HTTPS, since the token is sent with every request.
- The token is compared in constant time.
- To revoke the token, change it (or remove the provider). Every client using it stops working at once.

## Documentation
- Identity: https://www.trakhound.com/docs/trakhound-framework/identity
- Security: https://www.trakhound.com/docs/trakhound-framework/security
