# Validators

Validators verify incoming webhook requests before they are processed. Each endpoint can have one validator configured.

## Available validators

### Bearer token

Validates the `Authorization` header against a secret stored in an environment variable.

**Type:** `bearer`

```yaml
validation:
    auth:
        type: bearer
        token_env: MY_WEBHOOK_TOKEN
```

**Behavior:**

- Reads the `Authorization` header
- Strips `Bearer ` prefix if present (case-insensitive)
- Compares the token against the value of the env var specified by `token_env`
- Returns 401 if the token doesn't match or the env var is not set

### HMAC-SHA256

Computes an HMAC-SHA256 signature of the request body and compares it to the signature in the configured header.

**Type:** `hmac-sha256`

```yaml
validation:
    signature:
        header: X-Hub-Signature-256
        secret_env: GITHUB_WEBHOOK_SECRET
        algorithm: hmac-sha256
```

**Behavior:**

- Reads the signature from the specified header
- Strips algorithm prefix if present (e.g. `sha256=` from GitHub)
- Computes HMAC-SHA256 of the raw request body using the secret from the env var
- Uses constant-time comparison to prevent timing attacks
- Resets the request body position so the envelope builder can still read it
- Returns 401 if the signature doesn't match

**Compatible with:** GitHub, Shopify, and any provider using HMAC-SHA256 signatures.

### Stripe signature

Validates Stripe webhook signatures using Stripe's v1 signing scheme. Verifies both the HMAC-SHA256 signature and timestamp freshness (5-minute tolerance).

**Type:** `stripe-v1`

```yaml
validation:
    signature:
        header: Stripe-Signature
        secret_env: STRIPE_WEBHOOK_SECRET
        algorithm: stripe-v1
```

**Behavior:**

- Parses the `t=timestamp,v1=signature` header format
- Rejects requests with timestamps older than 5 minutes (replay protection)
- Computes HMAC-SHA256 of `timestamp.body` using the secret
- Uses constant-time comparison to prevent timing attacks
- Returns 401 if the signature doesn't match or timestamp is stale

### API key

Validates a custom header value against a secret stored in an environment variable. Supports any header name.

**Type:** `api-key`

```yaml
validation:
    auth:
        type: api-key
        header: X-API-Key
        token_env: MY_API_KEY
```

**Behavior:**

- Reads the value from the configured `header` name
- Compares against the env var specified by `token_env`
- Uses constant-time comparison to prevent timing attacks
- Returns 401 if the header is missing or the value doesn't match

### IP allowlist

Validates the remote IP address against a list of allowed IPs or CIDRs.

**Type:** `ip-allowlist`

```yaml
validation:
    auth:
        type: ip-allowlist
        token_env: ALLOWED_IPS
```

Set the env var to a comma-separated list:

```bash
ALLOWED_IPS=192.168.1.0/24,10.0.0.1,140.82.112.0/20
```

**Behavior:**

- Reads the allowlist from the env var as comma-separated IPs/CIDRs
- Supports both individual IPs and CIDR notation
- Normalizes IPv6-mapped IPv4 addresses
- Returns 401 if the remote IP is not in the allowlist

## Creating a custom validator

1. Create a class in `src/Hookpipe.Core/Validation/` implementing `IValidator`
2. Register it in the `validators` dictionary in `Program.cs`
3. Always reset `Request.Body.Position = 0` after reading the body
4. Use `CryptographicOperations.FixedTimeEquals` for signature comparison
5. Add XML docs
6. Add tests

See [CONTRIBUTING.md](../CONTRIBUTING.md) for details.
