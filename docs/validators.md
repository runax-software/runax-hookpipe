# Validators

Validators verify incoming webhook requests before they are processed. A webhook lists them under
`validate`; they run in order, and all must pass. The first one that fails returns 401.

```yaml
webhooks:
  github:
    validate:
      - type: hmac-sha256
        header: X-Hub-Signature-256
        secret: ${GITHUB_WEBHOOK_SECRET}
      - type: ip-allowlist
        allow: [192.30.252.0/22, 185.199.108.0/22, 140.82.112.0/20]
    to: rabbit:github.events
```

Secrets are written with `${…}` and resolved when the config loads, so a missing variable fails
startup instead of failing requests. Each type accepts only its own fields.

## Available validators

### Bearer token

Validates the `Authorization` header against a token.

**Type:** `bearer`

```yaml
- type: bearer
  token: ${MY_WEBHOOK_TOKEN}
```

**Behavior:**

- Reads the `Authorization` header
- Strips `Bearer ` prefix if present (case-insensitive)
- Compares the token against `token` in constant time
- Returns 401 if the token doesn't match

### HMAC-SHA256

Computes an HMAC-SHA256 signature of the request body and compares it to the signature in the configured header.

**Type:** `hmac-sha256`

```yaml
- type: hmac-sha256
  header: X-Hub-Signature-256
  secret: ${GITHUB_WEBHOOK_SECRET}
```

**Behavior:**

- Reads the signature from the specified header
- Strips algorithm prefix if present (e.g. `sha256=` from GitHub)
- Computes HMAC-SHA256 of the raw request body using `secret`
- Uses constant-time comparison to prevent timing attacks
- Resets the request body position so the envelope builder can still read it
- Returns 401 if the signature doesn't match

**Compatible with:** GitHub, Shopify, and any provider using HMAC-SHA256 signatures.

### Stripe signature

Validates Stripe webhook signatures using Stripe's v1 signing scheme. Verifies both the HMAC-SHA256 signature and timestamp freshness (5-minute tolerance).

**Type:** `stripe-v1`

```yaml
- type: stripe-v1
  secret: ${STRIPE_WEBHOOK_SECRET}
  header: Stripe-Signature        # optional; this is the default
```

**Behavior:**

- Parses the `t=timestamp,v1=signature` header format
- Rejects requests with timestamps older than 5 minutes (replay protection)
- Computes HMAC-SHA256 of `timestamp.body` using the secret
- Uses constant-time comparison to prevent timing attacks
- Returns 401 if the signature doesn't match or timestamp is stale

### API key

Validates a custom header value against a key. Supports any header name.

**Type:** `api-key`

```yaml
- type: api-key
  header: X-API-Key
  key: ${MY_API_KEY}
```

**Behavior:**

- Reads the value from the configured `header` name
- Compares against `key`
- Uses constant-time comparison to prevent timing attacks
- Returns 401 if the header is missing or the value doesn't match

### IP allowlist

Validates the remote IP address against a list of allowed IPs or CIDRs.

**Type:** `ip-allowlist`

```yaml
- type: ip-allowlist
  allow: [192.168.1.0/24, 10.0.0.1, 140.82.112.0/20]
```

`allow` can also be one comma-separated value, so the list can come from a variable:
`allow: ${ALLOWED_IPS}`.

**Behavior:**

- Supports both individual IPs and CIDR notation
- Normalizes IPv6-mapped IPv4 addresses
- Returns 401 if the remote IP is not in the allowlist

## Creating a custom validator

1. Create a class in `src/Runax.Hookpipe/Validation/` implementing `IValidator`
2. Add it to `ValidatorFactory.CreateAll()`, and list its required and optional fields in
   `HookpipeConfigurationBinder.ValidatorFields`
3. Always reset `Request.Body.Position = 0` after reading the body
4. Use `CryptographicOperations.FixedTimeEquals` for signature comparison
5. Add XML docs
6. Add tests

See [CONTRIBUTING.md](../CONTRIBUTING.md) for details.
