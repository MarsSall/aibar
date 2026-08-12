# AIBar Strong-Name Signing Policy

## Scope

Strong names provide assembly identity for future narrow `InternalsVisibleTo` relationships. A strong name does not provide a security sandbox against privileged code running in the same process.

## Public identity

- `AIBar.PublicKey.snk` contains public strong-name key material only.
- The expected public-key token is `0540e36870da7bad`.
- The expected SHA-256 of the assembly public-identity blob is `31484e36803555c161e7169682a8cc113ffd924b10866fd35f80c22cacd3e7aa`.
- Public identity changes require a reviewed update before any public-key-qualified friendship is introduced.

## Build modes

- Developer builds of signed authority-chain projects import `AIBar.Signing.props` and use `PublicSign` with the checked public key. Unit S activates no signed project or friendship; B2 remains responsible for applying this configuration to the new Core project.
- Official signing is CI-only. CI supplies private key material outside the repository, enables `AIBarOfficialSigning`, and passes the resulting temporary key location through `AIBarStrongNameKeyFile`.
- The official build must prove that its assembly public identity and token equal the checked public identity before publishing any artifact.
- Missing private material, use of the checked public key for official signing, or a public identity/token mismatch is a failed build.

## Secret custody and CI

- Private key material is stored only as a repository CI environment secret. Its identifier, value, and storage location are intentionally not recorded in source or evidence.
- CI decodes private material only into runner-owned temporary storage, never prints it, and removes that storage in an always-run cleanup step.
- CI logs may report only pass/fail, public identity, and public-key token. They must not report secret identifiers, private key bytes, temporary locations, credential data, or environment values.
- No package, publish output, SBOM, manifest, diagnostic, or repository file may contain private signing material.

## Rotation and revocation

- Maintainers own key rotation and revocation.
- Rotate by generating a replacement outside the repository, validating its public identity, updating this public identity policy, and revalidating every signed friend before activation.
- Revoke by disabling official signing and any affected friendship until a replacement identity is reviewed and available.

## Package and publish boundary

This policy authorizes no package publication or release. PublicSign is for clone/developer identity compatibility; only CI may produce an officially private-signed artifact.
