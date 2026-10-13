# Task 4 Report: Standalone Anchal HTTP host

## Outcome

Implemented the standalone `Anchal.Api` HTTP host under `/v1/api/anchal`, with monitoring controllers, cookie-backed monitoring authentication, route-gated no-store responses, freshness headers, rate limiting, and credentialed CORS restricted to `Cors:AllowedOrigins`.

## TDD and focused diagnosis

- Added credentialed login CORS coverage for an allowed configured origin and an unlisted origin.
- RED: before the API layer existed, the endpoint test failed to compile because `Anchal.Api.CommissionerMonitoring` was absent.
- During GREEN verification, the allowed-origin HTTP login returned `401 LOGIN` while a direct authenticator invocation succeeded.
- The request payload was a literal lower-case JSON object, `application/json; charset=utf-8`, using the same seeded `anchal1` / `Api-contract-password-123!` credentials as the direct call.
- A post-start direct-auth regression assertion then failed, proving the mismatch was fixture state rather than model binding or authentication.
- Root cause: `Guid.NewGuid()` was evaluated inside the test host's `AddDbContext` options lambda, creating a different in-memory database for each scope. The seed scope and request scope therefore used different stores.
- Correction: generate the in-memory database name once per host and reuse it for every context. No authentication behavior was redesigned.

## Verification

```text
dotnet test tests/Anchal.Tests/Anchal.Tests.csproj --filter FullyQualifiedName~CommissionerMonitoring.Api
Passed: 2, Failed: 0

dotnet test tests/Anchal.Tests/Anchal.Tests.csproj
Passed: 49, Failed: 0

git diff --check
exit 0
```

The test project continues to emit pre-existing NU1510 package-pruning warnings; they do not fail either suite.

## Correction round 1

### RED

Added HTTP contract regressions for malformed login JSON and monitoring login throttling. The focused suite failed as expected:

- malformed JSON returned `500 InternalServerError` rather than `400` with the standard `VALIDATION` envelope;
- throttling returned `429` without `Retry-After`.

### GREEN

- Restored bounded (50 KB) body parsing with malformed JSON translated to the monitoring validation envelope.
- Restored `Retry-After` metadata on rejected monitoring rate-limit leases.
- Restored active-office and service-id filter validation, conditional revision headers, standard internal error envelopes, and forwarded-header middleware for TLS-terminated deployments.

Focused endpoint verification after the correction:

```text
Passed: 4, Failed: 0
```
