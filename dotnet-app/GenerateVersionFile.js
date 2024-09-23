const fs = require('fs');
const path = require('path');

// Define paths for package.json and VersionInfo.cs
const packageJsonPath = path.join(__dirname, '..', 'package.json');
const versionInfoPath = path.join(__dirname, 'VersionInfo.cs');

// Check if VersionInfo.cs exists
const versionInfoExists = fs.existsSync(versionInfoPath);

// If VersionInfo.cs exists, compare file modification times
if (versionInfoExists) {
    const packageJsonStats = fs.statSync(packageJsonPath);
    const versionInfoStats = fs.statSync(versionInfoPath);

    // If package.json is older than VersionInfo.cs, exit
    if (versionInfoStats.mtime > packageJsonStats.mtime) {
        process.exit(0);
    }
}

// Read package.json
const packageJson = require(packageJsonPath);
const version = packageJson.version;

// C# class template with the version as a constant
const versionClassContent = `
// Auto-generated VersionInfo class
public static class VersionInfo
{
    public static readonly Version Version = new("${version}"); // This will be dynamically replaced by the actual version from package.json
}
`;

// Write the C# file
fs.writeFileSync(versionInfoPath, versionClassContent, 'utf8');

console.log(`VersionInfo.cs file generated with version: ${version}`);
