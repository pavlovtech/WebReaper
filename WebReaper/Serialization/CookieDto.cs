namespace WebReaper.Serialization;

/// <summary>
/// The flat, STJ-serialisable projection of a <see cref="System.Net.Cookie"/>
/// (ADR 0008). <see cref="System.Net.CookieContainer"/> does not JSON
/// round-trip and <see cref="System.Net.Cookie"/> is not source-gen-friendly;
/// the cookie payload shell maps to/from this DTO, so the
/// <see cref="System.Net.CookieContainer"/> ↔ <see cref="System.Net.CookieCollection"/>
/// quirk stays quarantined in the shell (ADR 0003) and the serialization
/// grammar is the AOT-clean source-gen one (no Newtonsoft).
/// </summary>
internal sealed record CookieDto(string Name, string Value, string Domain, string Path);
