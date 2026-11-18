# Minecraft Side Project

A MonoGame client that renders a 3D block world and a console Lidgren server.
The networking layer is adapted from
[MilleBo/SpeedCodingNetworkGame](https://github.com/MilleBo/SpeedCodingNetworkGame/tree/part_14_update_monogame_and_clean_up);
the world, blocks, and console server come from this repo.

There is no WinForms server, no enemies, no AI, no `GameRoom`, and no `Direction`
enum. Multiplayer is just named players moving in the shared 3D world.

## Projects

| Project | Type | Purpose |
| --- | --- | --- |
| `src/Minecraft.Client` | MonoGame desktop app | Renders the 3D world and remote players; owns the Lidgren client thread |
| `src/Minecraft.Server` | Console app | Owns the authoritative world and runs the packet server |
| `src/Minecraft.Shared` | Class library | Blocks, world generation, packet types, player models, and shared serialization |

## Running

From Git Bash:

```bash
./run.sh
```

That builds the solution, opens the server in its own console window, and starts
the client. Other modes:

```bash
./run.sh client    # build and run only the game client
./run.sh server    # build and run only the console server
./run.sh debug     # build, then open the workspace in VS Code if available
```

The client connects to `127.0.0.1:14241` and generates a short username from the
local account name. Change `Host` or `Port` in `src/Minecraft.Client/Game1.cs`
to point at another machine, and allow UDP `14241` through the firewall.

### Debugging

VS Code configurations:

| Configuration | What it does |
| --- | --- |
| `Client (Debug)` | Game client with the debugger attached |
| `Server (Debug)` | Console server in an integrated terminal, so commands work |
| `Client + Server (Debug)` | Both at once; stopping one stops both |

## Gameplay

```text
W / A / S / D        Move
Mouse                Look around
Left click           Break the outlined block (within 6 blocks)
Right click          Place the selected block on the outlined block's face
1 - 9 / mouse wheel  Choose which block to place
Space / Left Shift   Fly up / down
Escape               Pause / resume
Q                    Quit (while paused)
```

The crosshair marks the center of the view, and the block under it is outlined
in black. Left-clicking sends a `BlockUpdate` packet setting the target to air;
right-clicking places the selected block in the air cell next to the face you
are looking at. The server applies each edit to its authoritative world and
broadcasts it back to every client, so changes appear for everyone. The player
spawns just above the ground near the middle of the world, already in range of
blocks.

Remote players are blocky humanoids with a head, torso, two arms, and two legs,
tinted with a deterministic color derived from the username. Positions and view
angles are sent from the client to the server on a fixed 60 Hz network thread;
position updates use unreliable-sequenced delivery so a lost packet cannot
stall the stream. Clients render those updates ~100 ms in the past and
interpolate between snapshots, which smooths out jitter and short dropouts.

The body turns toward the direction of travel, while the head uses the remote
player's yaw and pitch and is clamped so it cannot spin around the neck. Arms
and legs swing with travel speed, with a subtle vertical bob; idle players
return to a resting pose. Each body part has its own transform, so animation is
done on the GPU without rebuilding meshes every network tick.

Names float above the heads. Labels are projected with the live viewport on
every frame, drawn at whole-pixel positions with point sampling, and backed by
a dark plate so they stay crisp and readable after a window resize. Your own
username is shown in the HUD, so names are visible even in a solo session.

The server runs block updates on a separate thread. Water behaves like a
simplified Minecraft fluid: `Water` is an infinite source, flow spreads
horizontally with decreasing strength (levels 1-7), columns fall and reset
their level, and flow disappears when its source is removed. Wheat grows
through seven stages when it has air above and dirt, grass, or sand below.

The client renders opaque and transparent blocks in separate passes. Water is
rendered as a translucent stepped mesh: side faces rise only to the flow
level and the top is two triangles, matching Minecraft's water levels. Wheat
uses crossed quads. Transparent blocks use alpha blending with depth writes
disabled, and the crosshair raycast skips non-solid blocks such as water and
crops.

Pressing `Escape` pauses the game: the overlay appears, the mouse is released,
and local movement, looking, block edits, and hotbar selection freeze. Server
messages are still drained while paused, so world edits and other players stay
in sync. Press `Escape` again to resume (the mouse re-centers), or `Q` to quit.
Being kicked from the server also pauses the game and shows the quit hint.

The console server keeps these commands:

```text
blocks                        List every registered block
stats                         Count blocks in the world
players                       List connected players
get <x> <y> <z>               Read a block
set <x> <y> <z> <block>       Place a block (updates all clients live)
kick <username>               Disconnect a player
help                          Show this help
quit                          Stop the server
```

## Networking

The transport is Lidgren.Network over UDP. The server listens on port `14241`
with the upstream app identifier `networkGame`. Client data packets use the
upstream layout: a `PacketType` byte, then a group id string, then the payload.
The group id for this game is `world`.

`PacketType` lives in `src/Minecraft.Shared/Net/PacketType.cs`:

| Packet | Direction | Purpose |
| --- | --- | --- |
| `Login` | client → server / server → client | Join with a username and receive the player list |
| `Input` | client → server | Send the local player's 3D position and view angles |
| `PlayerPosition` | server → client | One player's updated position |
| `AllPlayers` | server → client | Full player snapshot |
| `Kick` | server → clients | A named player was kicked |
| `WorldSnapshot` | server → client | Full block world on join |
| `BlockUpdate` | both directions | One block edit |

Packet handlers live in `src/Minecraft.Server/Packets` and follow the upstream
`IPacket` / `PacketFactory` pattern. The client manager is
`src/Minecraft.Client/Network/ManagerNetwork.cs`, adapted from upstream's
`ManagerNetwork` but running on a dedicated fixed 60 Hz thread.

Login uses Lidgren connection approval. Duplicate usernames are rejected with a
reason string. Login and world-snapshot packets are queued until Lidgren reports
the connection as `Connected`, so they are not dropped during the handshake.

## Adding a block

Add a member to `BlockId` and an entry to `BlockRegistry` in
`src/Minecraft.Shared/Blocks`. Set `IsTransparent`, `Alpha`, and `Shape`
(`Cube`, `Water`, or `Cross`) so the client renders the block with the right
pass and mesh. The client renderer and the server's `blocks` command read
from the same registry.
