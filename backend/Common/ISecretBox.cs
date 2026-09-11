namespace AltomateHR.Api.Common;

// Reversible encryption for secrets the app has to show back to the user.
//
// ⚠️ Not for authentication credentials — those are hashed, never recovered.
// See PasswordHasher.
public interface ISecretBox
{
    // Null in, null out. The blob is different every time even for the same
    // input, so a reader cannot tell two orgs share a value.
    string? Encrypt(string? plaintext);

    // Null for anything that will not decrypt — written under a previous key,
    // or tampered with. Never returns garbage: AES-GCM verifies the tag.
    string? Decrypt(string? stored);
}
