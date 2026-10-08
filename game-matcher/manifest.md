# Game Matcher v1 decisions

The v1 backend is an ASP.NET Core .NET 8 API with SQLite persistence. The connection
string is configurable as `ConnectionStrings:GameMatcher` (default:
`Data Source=game-matcher.db`). Swagger remains available in Development.

* An event is a weekly game with an explicit lifecycle: Draft, TeamsGenerated, Played,
  or Cancelled.
* Draws are supported. A draw contributes a 0.5 Elo outcome.
* Elo is recomputed from all recorded results in chronological order, starting every
  player at 1500. Goalkeepers use the same rating as other players; their goalkeeper
  flag influences team metadata and balancing only.
* Exactly two goalkeeper-flagged attending players are recommended. Designation and
  generation return warnings rather than silently changing flags, so a match can be
  managed while the roster is incomplete. Only attending players can be designated.
* Team generation creates size-constrained teams and balances Elo using a deterministic
  strongest-first assignment. Manual assignment replacement supports swaps and
  substitutes.
* The API scope is backend-only for v1; no frontend is included. The main resources
  are `/api/players` (roster CRUD) and `/api/events` (attendance, goalkeepers, teams,
  results, ratings, and history).
