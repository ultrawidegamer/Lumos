# Lumos

**Lumos** is a Unity editor tool that lets you quickly connect to Resonite via ResoniteLink and set up high quality baked lighting with ease


## Features

* Uses ResoniteLink to send and receive data from Resonite
* Download meshes from resonite assets, local assets or cache
* Generates secondary UVs for lightmapping
* Hierarchy generation with TRS data
* Solid baseline settings for baked lighting
* Simplifies light baking setup with an advanced settings toggle


## Requirements

* Resonite
* Unity

Currently only validated on
* Unity Editor 6000.3.6f1
* Windows 11


## Usage

1. Open Resonite and Unity
2. Open Lumos in Unity via "Tools" menu
3. Connect to cache and data folder (if required)
4. Enable and connect to ResoniteLink
5. Setup a [LumosConfig](#configuration)
6. Retrieve data from Resonite (may take some time)
7. Bake the lighting (may take some time)
8. Send scene to Resonite (may take some time)


## Configuration

To set up a LumosConfig, simply add a slot named "LumosConfig" to the root of the world. This step is optional, but without it, no lights will be sent to Unity, and all lighting will need to be configured manually for proper baked lighting to occur


| **Component**      | **Type**            | **Adding Type** | **Description** |
|--------------------|---------------------|-----------------|-----------------|
| ReferenceList      | Slot                | Manual          | List of slots and their children will be ignored during search |
| ReferenceList      | Light               | Manual          | List of lights that will be automatically setup in the scene |
| ValueField         | ColorX              | Automatic       | ColorX value that allows for adjusting the lightmaps color |
| BooleanValueDriver | TextureCompression? | Automatic       | Boolean to toggle all TextureCompression values between BC6H_LZMA (lossy) and RawRGBAHalf (lossless). Only use RawRGBAHalf if necessary and you understand the consequences, as it will significantly increase VRAM usage |

Adding type is either Manual or Automatic
* Manual - Component needs to be added manually to the LumosConfig slot before the step of Retrieving data from ResoniteLink
* Automatic - Component will be automatically added to the LumosConfig during the step of Sending Scene to Resonite

After sending the scene to Resonite, if you want to remove the baked lighting data and any related configuration items, there is a simple way to do it. A new slot called LumosAssets has been added to LumosConfig. Simply delete the LumosAssets slot, and it will automatically clean up all baked lighting data and settings.


## Known Issues

* Local assets not added by the current user of Lumos will not be downloaded as they are not easily accessible
* Blendshape and bone data is not currently supported
* Not all meshx file versions have been tested (old samples are hard to find) If a meshx file isnt working try pressing resave on the StaticMesh component to update it
* Procedural meshes are not currently supported. To resolve this, first bake the mesh as a StaticMesh
* Textures and materials are not currently properly supported, which can cause lighting and shadow accuracy issues that may require manual adjustments in Unity
* There is currently no automated way to specify backface global illumination
* ShadowCastMode is not currently respected
* Skybox textures are not supported


## License

Copyright (c) 2026, ultrawidegamer

All rights reserved.

Redistribution and use in source and binary forms, with or without modification, 
are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this list 
  of conditions and the following disclaimer.
  
* Redistributions in binary form must reproduce the above copyright notice, this 
  list of conditions and the following disclaimer in the documentation and/or other 
  materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND 
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED 
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, 
INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, 
BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, 
WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
POSSIBILITY OF SUCH DAMAGE.
