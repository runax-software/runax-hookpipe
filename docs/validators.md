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

## Creating a custom validator

1. Create a class in `src/Hookpipe.Core/Validation/` implementing `IValidator`
2. Register it in the `validators` dictionary in `Program.cs`
3. Always reset `Request.Body.Position = 0` after reading the body
4. Use `CryptographicOperations.FixedTimeEquals` for signature comparison
5. Add XML docs
6. Add tests

See [CONTRIBUTING.md](../CONTRIBUTING.md) for details.
