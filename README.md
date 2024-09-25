# Multiverse Engine (KERB branch)

20kdc experimental research branch of the [Multiverse fork](https://github.com/Space-Station-Multiverse/RobustToolbox) of Robust Toolbox.

_the point of this branch is to basically just throw commits at the wall and see if it can be done with no intention of being PRable anywhere_

Current features:

* New rendering abstraction, PAL; Clyde refactored to be based on it
* Content is now, in theory at least, able to do highly complex performant custom rendering
* Basically all the content benefits of the WebGPU branch but it's actually vaguely finished and it should work on GLES2 platforms as good as the base version

(Upstream info follows)

---

![Robust Toolbox](https://raw.githubusercontent.com/space-wizards/asset-dump/3dd3078e49e3a7e06709a6e0fc6e3223d8d44ca2/robust.png)

Robust Toolbox is an engine primarily being developed for [Space Station 14](https://github.com/space-wizards/space-station-14), although we're working on making it usable for both [singleplayer](https://github.com/space-wizards/RobustToolboxTemplateSingleplayer) and [multiplayer](https://github.com/space-wizards/RobustToolboxTemplate) projects.

Use the [content repo](https://github.com/space-wizards/space-station-14) for actual development, even if you're modifying the engine itself.

## Project Links

[Website](https://spacestation14.io/) | [Discord](https://discord.gg/t2jac3p) | [Forum](https://forum.spacestation14.io/) | [Steam](https://store.steampowered.com/app/1255460/Space_Station_14/) | [Standalone Download](https://spacestation14.io/about/nightlies/)

## Documentation/Wiki

The [wiki](https://docs.spacestation14.io/) has documentation on SS14s content, engine, game design and more. We also have lots of resources for new contributors to the project.

## Contributing

We are happy to accept contributions from anybody. Get in Discord or IRC if you want to help. We've got a [list of issues](https://github.com/space-wizards/RobustToolbox/issues) that need to be done and anybody can pick them up. Don't be afraid to ask for help either!

## Building

This repository is the **engine** part of SS14. It's the base engine all SS14 servers will be built on. As such, it does not start on its own: it needs the [content repo](https://github.com/space-wizards/space-station-14). Think of Robust Toolbox as BYOND in the context of Space Station 13.

## Legal Info

See [legal.md](https://github.com/space-wizards/RobustToolbox/blob/master/legal.md) for licenses and copyright.
