# UniVRM-Fast

[![GitHub latest release](https://img.shields.io/github/v/release/vrm-c/UniVRM?color=green)](https://github.com/GeneTailor/UniVRM-Fast/releases/latest)
[![GitHub license](https://img.shields.io/github/license/vrm-c/UniVRM)](https://github.com/vrm-c/UniVRM/blob/master/LICENSE.txt)

This is a fork of the UniVRM package with a focus on faster import and export times.
So far potentially 2x faster than the base UniVRM (though some of the changes need to be confirmed to be robust).


## Installation

### Latest Release

[Download here](https://github.com/GeneTailor/UniVRM-Fast/releases/latest)


You can install UniVRM-Fast using the UPM Package.

### UPM Package

From the [latest release](https://github.com/GeneTailor/UniVRM-Fast/releases/latest), you can find UPM package urls.

- For import/export VRM 1.0
  - You have to install all of the following UPM packages:
    - `com.vrmc.gltf`
    - `com.vrmc.vrm`
- For import/export VRM 0.x
  - You have to install all of the following UPM packages:
    - `com.vrmc.gltf`
    - `com.vrmc.univrm`
- For import/export glTF 2.0
  - You have to install all of the following UPM packages:
    - `com.vrmc.gltf`

You can install these UPM packages via `Package Manager` -> `+` -> `Add package from git URL...` in UnityEditor.

## License

- [MIT License](./LICENSE.txt)
