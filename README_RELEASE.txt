Stats & Expiration Mod — PBOX3 Release Source

Replace/build with these files:
Root:
- ExpirationSaveManager.cs
- ExpirationManager.cs
- ProductExpirationComponent.cs
- ExpirationLoadFinalizer.cs
- ExpirationSafetyMigration.cs

Patches:
- BoxPatches.cs
- BoxExpirationLabel.cs

Before publishing:
1. Set the new mod version in Plugin.cs.
2. Compile the project normally.
3. Put the compiled release DLL/files into your normal Nexus archive.
4. Use NEXUS_CHANGELOG.txt / NEXUS_UPDATE_NOTES.txt for the Nexus page.

This release source has verbose DebugLog calls removed.
Warnings/errors and the one-time ExpiryRescue completion summary are retained.
