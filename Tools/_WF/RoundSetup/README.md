# Local Round Setup playtest

Build the server and client, then start a local server with these explicit overrides:

```text
--cvar game.lobbyenabled=true --cvar game.lobbyduration=360 --cvar game.defaultpreset=MonoStandard --cvar wf.planet_networks=true --cvar wf.round_setup.demo_enabled=true --cvar wf.round_setup.demo_host=localhost@RoundSetupHost
```

Use a local server configuration with authentication disabled and hub advertising off. The demo setting is server-only, false by default and is not archived. It does not change player bank balances, pause the clock or bypass ordinary job eligibility.

After the server is ready, run `Tools/_WF/RoundSetup/start-test-host.ps1` from PowerShell and connect the visible client under a different player name. Pass `-Port` if the server uses a port other than 1212. The script starts a real headless client and returns its process ID and log paths; retain that ID to stop this playtest host afterward. It neither builds nor starts a server.

Local unauthenticated connections receive the `localhost@` prefix on the server, so the demo host setting includes that prefix. The headless launcher disables texture preloading because it has no graphics context.

Once the named host's normal player data has loaded, the server creates one public Crashlanded lobby. The host selects Vagrant (the Contractor job), readies up and advertises three unoccupied Vagrant openings. Late joining is enabled. In the visible client, open Crew Setup, choose this lobby, take a Vagrant opening and confirm with the ready button. Mid-round, use Join Crew and then Join Round inside the crew setup. The ordinary six-minute pregame clock remains in effect. The crashing ship's separate preparation warning still lasts three minutes after the first spawn.

Seeding happens once per round, so closing the demo lobby does not recreate it. Round restart allows another demo while the flag remains enabled. A host that already created or joined a lobby is left alone. The headless host must remain connected for round-start spawning. Disable `wf.round_setup.demo_enabled` or omit the override for normal play.
