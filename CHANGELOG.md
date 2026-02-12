# Change Log

All notable changes to the "fast-build" extension will be documented in this file.

Check [Keep a Changelog](http://keepachangelog.com/) for recommendations on how to structure this file.

## [Unreleased]

## [0.0.7] - 2026-02-12

### Features
- Added MSBuild performance optimizations to fastbuild.csproj generation for faster builds.
- Implemented simplified DLL copy functionality to streamline build processes.
- Added compatibility mode setting to revert to behaviors from v0.0.6, disabling MSBuild optimizations, DLL copy, and path-finding changes for users needing stability.

### Improvements
- Enhanced path finding to prioritize checking directories for csproj files when starting from a directory.
