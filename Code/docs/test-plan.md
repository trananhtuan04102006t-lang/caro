# Test Plan

## Automated tests

- [ ] Rule engine: horizontal win.
- [ ] Rule engine: vertical win.
- [ ] Rule engine: diagonal win.
- [ ] Rule engine: invalid move outside board.
- [ ] Rule engine: invalid move on occupied cell.
- [ ] Protocol: frame writer writes length-prefix correctly.
- [ ] Protocol: frame reader handles split TCP reads.
- [ ] Protocol: malformed JSON returns protocol error.
- [ ] Server: create room.
- [ ] Server: join room.
- [ ] Server: reject move from wrong player.
- [ ] Server: broadcast state after valid move.
- [x] Storage: save and read completed match history.
- [x] Storage: reject unfinished match history.
- [x] Storage: keep in-memory match snapshots isolated from caller mutation.
- [x] Storage: list best player records by win rate and wins.

## Manual tests

- [ ] Run server and two clients on the same machine.
- [ ] Run server and two clients over LAN.
- [ ] Disconnect one client during a match.
- [ ] Reconnect within the supported reconnect window.
- [ ] Send chat while playing.
- [ ] Finish a match and save history locally.

## Demo acceptance

- [ ] A full 1v1 match can be completed without using a console client or web app.
- [ ] Server remains stable when a client disconnects.
- [ ] README explains how to build, run and test the project.
- [ ] Protocol documentation matches the implemented message names.
