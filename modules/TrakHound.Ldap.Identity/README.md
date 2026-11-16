# TrakHound.Ldap.Identity

An Identity Provider that signs users in with a username and password verified by an LDAP server (ex. Active Directory or OpenLDAP). The user's groups become their Roles. Use it when your users already have accounts in a directory and you want them to sign in to Apps and Apis on an Instance with those accounts.

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.Ldap.Identity` |
| Category | `identity` |

## How it signs users in
Users enter their username and password on the Instance's sign in page (`/_identity/signin/{providerId}`). The Instance applies its delay and lockout after repeated wrong passwords, then the provider checks the password with the LDAP server. A successful sign in starts an Instance session (a cookie), so the LDAP server isn't called again on each request.

The password is always verified by binding to the server as the user. The provider finds the user's DN in one of two ways:

- **Direct bind** (`bindDnFormat`): the username is put into a format to make the bind name, ex. `{username}@corp.example.com` (Active Directory) or `uid={username},ou=people,dc=example,dc=com`. Set `searchBase` too if you want the user's groups, name and email, which are read by searching as the user after the bind. Without `searchBase` the user has no Roles.
- **Service account** (`serviceBindDn`): the provider binds as a service account, searches `searchBase` with `userFilter` for exactly one user, then binds as that user's DN with their password. Use this when the bind name can't be built from the username. `serviceBindDn` is used instead of `bindDnFormat` when both are set.

The signed in identity has:

| Property | Value |
|---|---|
| User ID | The username as entered (trimmed) |
| Name | The `nameAttribute` value, or the username if there isn't one |
| Roles | The values of `groupAttribute`, as the group's CN (ex. `CN=Operators,OU=Groups,DC=corp` gives `Operators`) or its full DN with `roleFormat: dn` |
| Properties | `dn` (the user's DN, when the user was found by a search) and `email` (the `mail` attribute) |

Grant those Roles permissions on your Apps and Apis with a Security Profile that uses this provider (Config > Access > Security Profiles).

## Configuration
Add the provider on Security > Providers > **Add Identity Provider**, choose this package and set its Parameters.

| Parameter | Required | Default | Description |
|---|---|---|---|
| `server` | Yes | | The LDAP server's host name |
| `port` | No | `636` with `useSsl`, otherwise `389` | The server's port |
| `useSsl` | No | `false` | Connect with LDAPS |
| `startTls` | No | `false` | Upgrade the connection with StartTLS |
| `bindDnFormat` | One of `bindDnFormat` or `serviceBindDn` | | The bind name for the user, with `{username}` where the username goes (ex. `{username}@corp.example.com`) |
| `serviceBindDn` | One of `bindDnFormat` or `serviceBindDn` | | The DN of a service account used to search for the user (ex. `CN=svc-trakhound,OU=Service Accounts,DC=corp,DC=example,DC=com`) |
| `serviceBindPassword` | With `serviceBindDn` | | The service account's password |
| `searchBase` | With `serviceBindDn` | | The DN to search for users under (ex. `OU=Users,DC=corp,DC=example,DC=com`). Optional with `bindDnFormat`, where it's used to read the user's groups, name and email |
| `userFilter` | No | `(sAMAccountName={username})` | The search filter for the user. Use `(uid={username})` for OpenLDAP |
| `groupAttribute` | No | `memberOf` | The attribute that lists the user's groups |
| `nameAttribute` | No | `displayName` | The attribute for the user's display name |
| `roleFormat` | No | `cn` | `cn` to use each group's CN as the Role, or `dn` to use its full DN |

Example Parameters for Active Directory with a service account:

```yaml
server: dc01.corp.example.com
useSsl: true
serviceBindDn: CN=svc-trakhound,OU=Service Accounts,DC=corp,DC=example,DC=com
serviceBindPassword: <service-account-password>
searchBase: OU=Users,DC=corp,DC=example,DC=com
```

The sign in page can be changed with the Instance's `signInProduct`, `signInLogo` and `signInRoute` Parameters (see the Identity documentation).

## Security
- Turn on `useSsl` or `startTls`. Without either, passwords are sent to the LDAP server in clear text, and the provider logs a warning when it starts.
- Parameters whose names contain `password` (ex. `serviceBindPassword`) are encrypted when the configuration is saved.
- Give the service account read access only. It's used to search for users, never to change anything.
- An empty password is always refused, since many LDAP servers accept a bind with no password as an anonymous bind.
- The username is escaped before it's put into `userFilter` or a DN `bindDnFormat`, so it can't change the filter or the DN.
- If the search finds more than one user, the sign in fails. Make `userFilter` match a single account.
- A wrong password counts toward the Instance's sign in lockout. An LDAP server that can't be reached doesn't: the user sees "Sign in is not available" and the error is written to the provider's log.
- Each connection to the server times out after 15 seconds. Referrals aren't followed.

## Development
Test the configuration on Security > Providers with **Test Sign In**, which opens the provider's sign in page in a new tab. Failed sign ins and their reasons are written to the `security` log.

## Documentation
- Identity: https://www.trakhound.com/docs/trakhound-framework/identity
- Security: https://www.trakhound.com/docs/trakhound-framework/security
