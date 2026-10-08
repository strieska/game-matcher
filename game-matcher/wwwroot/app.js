const $ = (selector, root = document) => root.querySelector(selector);
const state = { organizer: false, csrf: "", players: [], games: [], tab: "games", eventId: null,
  selected: new Set(), keepers: new Set(), assignments: [], dirtyTeams: false, dirtyAttendance: false,
  busy: false, editingPlayer: null, addToEvent: false };
const content = $("#content");
const escapeHtml = value => String(value ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const fullName = player => `${player.name} ${player.surname}`.trim();
const activePlayers = () => state.players.filter(p => p.isActive);
const currentGame = () => state.games.find(e => e.id === state.eventId);
const editable = game => state.organizer && game && !["Played", "Cancelled"].includes(game.status);
const formatDate = date => new Date(date).toLocaleString(undefined, { weekday: "short", month: "short", day: "numeric", year: "numeric", hour: "2-digit", minute: "2-digit" });
const localInputDate = date => {
  const d = date ? new Date(date) : new Date();
  return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
};

function notice(message, error = false) {
  const el = $("#notice");
  el.textContent = message;
  el.classList.toggle("error", error);
  el.hidden = !message;
  el.setAttribute("role", error ? "alert" : "status");
}

async function api(path, method = "GET", body) {
  const response = await fetch(`/api${path}`, {
    method, credentials: "same-origin", headers: {
      ...(body !== undefined ? { "Content-Type": "application/json" } : {}),
      ...(method !== "GET" ? { "X-CSRF-TOKEN": state.csrf } : {})
    }, ...(body !== undefined ? { body: JSON.stringify(body) } : {})
  });
  const text = await response.text();
  let data;
  try { data = text ? JSON.parse(text) : null; }
  catch { throw new Error(`Server returned an unreadable response (${response.status}).`); }
  if (!response.ok) {
    if (response.status === 401 && path !== "/auth/login") {
      state.organizer = false;
      $("#session-button").textContent = "Organizer sign in";
      render();
    }
    throw new Error(data?.detail || Object.values(data?.errors || {}).flat().join(" ") || data?.title || `Request failed (${response.status}).`);
  }
  return data;
}

async function session() {
  const result = await api("/auth/session");
  state.organizer = result.isOrganizer;
  state.csrf = result.csrfToken;
  $("#session-button").textContent = state.organizer ? "Sign out" : "Organizer sign in";
}

async function load() {
  const [players, games] = await Promise.all([api("/players"), api("/events/history")]);
  state.players = players;
  state.games = games;
}

function selectGame(id) {
  state.eventId = id;
  const game = currentGame();
  state.selected = new Set(game?.attendances.filter(a => a.status === "Attending").map(a => a.playerId) || []);
  const assigned = game?.assignments.filter(a => a.isGoalkeeper).map(a => a.playerId) || [];
  state.keepers = new Set(assigned.length ? assigned : state.players.filter(p => p.isGoalkeeper && state.selected.has(p.id)).map(p => p.id));
  state.assignments = game?.assignments.filter(a => a.teamNumber > 0).map(a => ({ ...a })) || [];
  state.dirtyTeams = false;
  state.dirtyAttendance = false;
}

async function action(work, message, refresh = true) {
  if (state.busy) return;
  state.busy = true;
  content.setAttribute("aria-busy", "true");
  document.querySelectorAll("button, input, select").forEach(el => {
    if (!el.disabled) { el.disabled = true; el.dataset.busyDisabled = "true"; }
  });
  try {
    await work();
    if (refresh) {
      await load();
      selectGame(state.eventId);
      render();
    }
    notice(message);
  } catch (error) {
    notice(error.message, true);
  } finally {
    state.busy = false;
    content.setAttribute("aria-busy", "false");
    document.querySelectorAll("[data-busy-disabled]").forEach(el => { el.disabled = false; delete el.dataset.busyDisabled; });
  }
}

function confirmAction(title, message, label = "Confirm") {
  const dialog = $("#confirm-dialog");
  $("#confirm-title").textContent = title;
  $("#confirm-message").textContent = message;
  $("#confirm-submit").textContent = label;
  dialog.returnValue = "cancel";
  dialog.showModal();
  return new Promise(resolve => dialog.addEventListener("close", () => resolve(dialog.returnValue === "confirm"), { once: true }));
}

function badge(status) {
  return `<span class="pill ${status.toLowerCase()}">${({ Draft: "Setting up", TeamsGenerated: "Teams ready", Played: "Completed", Cancelled: "Cancelled" })[status]}</span>`;
}

function render() {
  content.setAttribute("aria-busy", "false");
  content.setAttribute("aria-label", state.tab);
  document.querySelectorAll("[data-tab]").forEach(button => {
    const active = button.dataset.tab === state.tab;
    button.classList.toggle("active", active);
    if (active) button.setAttribute("aria-current", "page");
    else button.removeAttribute("aria-current");
  });
  if (state.tab === "players") renderPlayers();
  else if (state.eventId && currentGame()) renderGame();
  else renderGames();
}

function renderGames() {
  const history = state.tab === "history";
  const games = state.games.filter(e => history ? ["Played", "Cancelled"].includes(e.status) : !["Played", "Cancelled"].includes(e.status));
  content.innerHTML = `
    <div class="section-heading"><div><h2>${history ? "Past games" : "Game day"}</h2><p class="muted">${history ? "Every lineup. Every result." : "Your next great game starts here."}</p></div><span class="pill">${games.length} games</span></div>
    ${!history && state.organizer ? `<form id="create-game" class="panel field-row"><label>Game date & time<input name="date" type="datetime-local" value="${localInputDate()}" required></label><button class="button primary" type="submit">New game</button></form>` : ""}
    ${games.length ? games.map(e => `<article class="game-card"><div class="section-heading"><div><h3>${escapeHtml(formatDate(e.scheduledAt))}</h3><p class="muted">${e.attendances.filter(a => a.status === "Attending").length} players${e.result ? ` &middot; ${escapeHtml(e.result.winner)}` : ""}</p></div>${badge(e.status)}</div>
    <div class="actions">${e.result ? `<span class="score">${e.result.scoreA} : ${e.result.scoreB}</span>` : ""}<button type="button" class="button subtle" data-open="${e.id}">${state.organizer && editable(e) ? "Manage game" : "View game"}</button></div></article>`).join("") :
      `<div class="empty"><h2>${history ? "A clean slate" : "No upcoming games"}</h2><p>${history ? "Completed games and results will appear here." : state.organizer ? "Create a game, pick who's here, and find your teams." : "The organizer hasn't scheduled the next game yet."}</p></div>`}`;
}

function rosterWarning() {
  const count = activePlayers().filter(p => p.isGoalkeeper).length;
  return count === 2 ? "" : `<p class="warning">The roster has ${count} dedicated goalkeepers; two are recommended. Each game still needs exactly two assigned goalkeepers, including substitutes.</p>`;
}

function renderPlayers() {
  content.innerHTML = `<div class="section-heading"><div><h2>The roster</h2><p class="muted">Familiar faces. Fresh talent. One team.</p></div>${state.organizer ? '<button type="button" class="button primary" data-add-player>Add player</button>' : ""}</div>
    ${state.organizer ? rosterWarning() : ""}
    <label>Find a player<input type="search" id="player-search" placeholder="Search names..." autocomplete="off"></label>
    <div id="player-list" class="stack" style="margin-top:16px"></div>`;
  renderPlayerList("");
}

function renderPlayerList(query) {
  const players = activePlayers().filter(p => fullName(p).toLocaleLowerCase().includes(query.toLocaleLowerCase()))
    .sort((a, b) => b.elo - a.elo || fullName(a).localeCompare(fullName(b)));
  $("#player-list").innerHTML = players.length ? players.map(p => `<div class="player-row"><div><span class="player-name">${escapeHtml(fullName(p))}</span><span class="player-details">${p.isGoalkeeper ? "Goalkeeper &middot; " : ""}${p.matchesPlayed} games &middot; ${p.wins} W / ${p.draws} D / ${p.losses} L${p.isGoalkeeper || p.goalkeeperGoalsConceded ? ` &middot; ${p.goalkeeperGoalsConceded} conceded as GK` : ""}</span></div><div class="actions"><span class="pill">${p.elo} Elo</span>${state.organizer ? `<button type="button" class="button subtle" data-edit-player="${p.id}">Edit</button><button type="button" class="button subtle danger" data-archive="${p.id}">Archive</button>` : ""}</div></div>`).join("") :
    '<div class="empty">No players found.</div>';
}

function renderGame() {
  const e = currentGame();
  const canEdit = editable(e);
  content.innerHTML = `<button type="button" class="button subtle" data-back>Back to ${state.tab === "history" ? "history" : "games"}</button>
    <div class="section-heading" style="margin-top:20px"><div><h2>${escapeHtml(formatDate(e.scheduledAt))}</h2><p class="muted">${e.result ? `Final whistle &middot; ${escapeHtml(e.result.winner)}` : "Good company. A fair game."}</p></div>${badge(e.status)}</div>
    ${canEdit ? `<details class="panel"><summary>Edit game details</summary><form id="edit-game" class="field-row" style="margin-top:16px"><label>Date & time<input type="datetime-local" name="date" value="${localInputDate(e.scheduledAt)}" required></label><button class="button subtle" type="submit">Save date</button><button class="button danger subtle" type="button" data-cancel>Cancel game</button></form></details>` : ""}
    ${canEdit && e.status === "Draft" ? `<section class="panel"><div class="section-heading"><div><h3>Who's playing?</h3><p id="attendance-count" class="muted"></p></div><button type="button" class="button subtle" data-add-player>Add someone new</button></div>
      ${rosterWarning()}<label>Find a player<input id="attendance-search" type="search" placeholder="Search your roster..." autocomplete="off"></label>
      <div id="attendance-list" class="roster"></div><div class="actions"><button type="button" class="button subtle" data-save-attendance>Save attendance</button></div>
      <div style="margin-top:24px"><h3>Two goalkeepers. One per team.</h3><p class="help">Select the people keeping goal today. An outfield player can step in without changing their roster role.</p><div class="field-row" id="keeper-fields"></div></div>
      <p class="help">Initializing saves attendance and goalkeeper choices, then generates balanced teams.</p><button class="button primary" type="button" data-generate>Initialize game</button></section>` : ""}
    ${e.status === "TeamsGenerated" && canEdit ? `<div class="panel"><h3>Your teams are ready</h3><p class="help">Drag players between teams, or use their Move buttons on your phone. Swap goalkeepers by moving both, or mark a replacement. Save when each team has one goalkeeper and team sizes differ by at most one.</p><div class="actions"><button type="button" class="button primary" data-save-teams ${!state.dirtyTeams ? "disabled" : ""}>Save adjustments</button><button type="button" class="button subtle" data-reset-teams ${!state.dirtyTeams ? "disabled" : ""}>Discard adjustments</button><button type="button" class="button subtle" data-edit-attendance>Change attendance</button><button type="button" class="button subtle" data-regenerate>Regenerate teams</button></div><p id="team-edit-status" class="help" role="status">${state.dirtyTeams ? "Unsaved adjustments" : "All adjustments saved"}</p></div>` : ""}
    <div id="team-view"></div>
    ${e.result ? `<section class="panel"><div class="section-heading"><h3>Final score</h3><span class="pill played">Locked</span></div><div class="score">Team A ${e.result.scoreA} : ${e.result.scoreB} Team B</div><p class="muted">${escapeHtml(e.result.winner)}. Elo has been recalculated for everyone who played.</p><p class="help">This game's attendance, teams, and score are now read-only.</p></section>` :
      e.status === "TeamsGenerated" && canEdit ? `<form id="result-form" class="panel result-entry"><h3>Final whistle</h3><p class="help">Saving the score completes and locks this game. Check the teams and score first.</p><div class="score-fields"><label>Team A score<input name="scoreA" type="number" min="0" max="2147483647" step="1" required inputmode="numeric"></label><label>Team B score<input name="scoreB" type="number" min="0" max="2147483647" step="1" required inputmode="numeric"></label></div><button class="button primary" type="submit" style="margin-top:16px" ${state.dirtyTeams ? "disabled" : ""}>Finish game & update Elo</button></form>` : ""}
    ${!canEdit && !e.assignments.some(a => a.teamNumber > 0) ? '<div class="empty">No teams have been generated for this game.</div>' : ""}`;
  if ($("#attendance-list")) {
    renderAttendance("");
    renderKeeperFields();
  }
  renderTeams();
}

function renderAttendance(query) {
  const players = activePlayers().filter(p => fullName(p).toLocaleLowerCase().includes(query.toLocaleLowerCase()));
  $("#attendance-count").textContent = `${state.selected.size} players selected`;
  $("#attendance-list").innerHTML = players.length ? players.map(p => `<label class="player-row ${state.selected.has(p.id) ? "selected" : ""}"><span class="check-label"><input type="checkbox" data-attend="${p.id}" ${state.selected.has(p.id) ? "checked" : ""}><span class="player-name">${escapeHtml(fullName(p))}<span class="player-details">${p.isGoalkeeper ? "Goalkeeper" : "Outfield"}</span></span></span><span class="pill">${p.elo}</span></label>`).join("") :
    '<p class="muted">No players found. Add someone new above.</p>';
}

function renderKeeperFields() {
  state.keepers = new Set([...state.keepers].filter(id => state.selected.has(id)));
  const ids = [...state.keepers];
  $("#keeper-fields").innerHTML = [0, 1].map(i => `<label>Goalkeeper ${i + 1}<select data-keeper="${i}"><option value="">Select goalkeeper...</option>${activePlayers().filter(p => state.selected.has(p.id)).map(p => `<option value="${p.id}" ${ids[i] === p.id ? "selected" : ""}>${escapeHtml(fullName(p))}${p.isGoalkeeper ? " (dedicated)" : " (substitute)"}</option>`).join("")}</select></label>`).join("");
}

function renderTeams() {
  const target = $("#team-view");
  if (!target || !state.assignments.length) return;
  const canEdit = editable(currentGame()) && currentGame().status === "TeamsGenerated";
  target.innerHTML = `<div class="teams">${[1, 2].map(team => {
    const players = state.assignments.filter(a => a.teamNumber === team).sort((a, b) => Number(b.isGoalkeeper) - Number(a.isGoalkeeper) || a.playerId - b.playerId);
    const sum = players.reduce((total, a) => total + a.player.elo, 0);
    return `<section class="team" data-team="${team}" aria-label="Team ${team === 1 ? "A" : "B"}"><div class="team-header"><div><h3>Team ${team === 1 ? "A" : "B"}</h3><p>${players.length} players &middot; ${sum} total Elo</p></div><span class="pill">${players.length ? Math.round(sum / players.length) : 0} avg</span></div>
      ${players.map(a => `<div class="team-player" data-player="${a.playerId}" ${canEdit ? 'draggable="true"' : ""}><div class="section-heading" style="margin:0"><span class="player-name">${escapeHtml(fullName(a.player))}</span><span class="player-details">${a.player.elo} Elo</span></div>
        ${a.isGoalkeeper ? `<span class="pill">Goalkeeper${a.isSubstitute ? " &middot; stepping in" : ""}</span>` : ""}
        ${canEdit ? `<div class="actions"><button type="button" class="button subtle" data-move="${a.playerId}" aria-label="Move ${escapeHtml(fullName(a.player))} to Team ${team === 1 ? "B" : "A"}">Move to ${team === 1 ? "B" : "A"}</button><button type="button" class="button subtle" data-toggle-keeper="${a.playerId}">${a.isGoalkeeper ? "Make outfield" : "Make goalkeeper"}</button></div>` : ""}</div>`).join("")}</section>`;
  }).join("")}</div>`;
}

function changeTeams(id, team) {
  const assignment = state.assignments.find(a => a.playerId === id);
  if (!assignment || !editable(currentGame()) || currentGame().status !== "TeamsGenerated" || state.busy) return;
  assignment.teamNumber = team;
  teamsChanged();
}

function teamsChanged() {
  state.dirtyTeams = true;
  renderTeams();
  $("[data-save-teams]").disabled = false;
  $("[data-reset-teams]").disabled = false;
  $("#result-form button").disabled = true;
  $("#team-edit-status").textContent = "Unsaved adjustments. Save before finishing the game.";
}

function openPlayer(id = null) {
  state.editingPlayer = id;
  state.addToEvent = !id && state.tab === "games" && currentGame()?.status === "Draft";
  const form = $("#player-form");
  form.reset();
  $("#player-error").hidden = true;
  const player = state.players.find(p => p.id === id);
  $("#player-title").textContent = player ? "Edit player" : "New player";
  if (player) {
    form.elements.name.value = player.name;
    form.elements.surname.value = player.surname;
    form.elements.isGoalkeeper.checked = player.isGoalkeeper;
  }
  $("#player-dialog").showModal();
}

async function saveAttendance() {
  const requests = activePlayers().map(p => ({ playerId: p.id, status: state.selected.has(p.id) ? "Attending" : "Absent" }));
  await api(`/events/${state.eventId}/attendance`, "PUT", requests);
}

content.addEventListener("input", event => {
  if (event.target.id === "player-search") renderPlayerList(event.target.value);
  if (event.target.id === "attendance-search") renderAttendance(event.target.value);
});

content.addEventListener("change", event => {
  const input = event.target;
  if (input.dataset.attend) {
    const id = Number(input.dataset.attend);
    if (input.checked) {
      state.selected.add(id);
      if (state.players.find(p => p.id === id)?.isGoalkeeper && state.keepers.size < 2) state.keepers.add(id);
    } else { state.selected.delete(id); state.keepers.delete(id); }
    state.dirtyAttendance = true;
    renderAttendance($("#attendance-search").value);
    renderKeeperFields();
  }
  if (input.dataset.keeper !== undefined) {
    state.keepers = new Set([...document.querySelectorAll("[data-keeper]")].map(s => Number(s.value)).filter(Boolean));
    state.dirtyAttendance = true;
  }
});

content.addEventListener("click", async event => {
  const button = event.target.closest("button");
  if (!button || button.disabled || state.busy) return;
  if (button.dataset.open) { selectGame(Number(button.dataset.open)); render(); }
  if (button.hasAttribute("data-back")) {
    if ((state.dirtyTeams || state.dirtyAttendance) && !await confirmAction("Leave unsaved changes?", "Your unsaved selections and adjustments will be discarded.", "Leave")) return;
    state.eventId = null; render();
  }
  if (button.hasAttribute("data-add-player")) openPlayer();
  if (button.dataset.editPlayer) openPlayer(Number(button.dataset.editPlayer));
  if (button.dataset.archive) {
    const id = Number(button.dataset.archive);
    if (await confirmAction("Archive player?", "They will leave the available roster, but their games and ratings will remain in history.", "Archive"))
      await action(() => api(`/players/${id}`, "DELETE"), "Player archived.");
  }
  if (button.hasAttribute("data-save-attendance")) {
    const keepers = [...document.querySelectorAll("[data-keeper]")].map(s => Number(s.value));
    await action(async () => {
      await saveAttendance();
      if (keepers.every(Boolean) && new Set(keepers).size === 2)
        await api(`/events/${state.eventId}/goalkeepers`, "PUT", keepers);
    }, "Attendance saved.");
  }
  if (button.hasAttribute("data-generate")) {
    const id = state.eventId;
    const keepers = [...document.querySelectorAll("[data-keeper]")].map(s => Number(s.value));
    if (state.selected.size < 4) { notice("Select at least four players.", true); return; }
    if (keepers.some(id => !id) || new Set(keepers).size !== 2) { notice("Select two different attending goalkeepers.", true); return; }
    await action(async () => {
      await saveAttendance();
      await api(`/events/${id}/goalkeepers`, "PUT", keepers);
      await api(`/events/${id}/teams`, "POST", { teamCount: 2 });
    }, "Game initialized. Your teams are ready.");
  }
  if (button.dataset.move) {
    const id = Number(button.dataset.move);
    const a = state.assignments.find(a => a.playerId === id);
    changeTeams(id, a.teamNumber === 1 ? 2 : 1);
  }
  if (button.dataset.toggleKeeper) {
    const a = state.assignments.find(a => a.playerId === Number(button.dataset.toggleKeeper));
    a.isGoalkeeper = !a.isGoalkeeper;
    a.isSubstitute = a.isGoalkeeper && !a.player.isGoalkeeper;
    teamsChanged();
  }
  if (button.hasAttribute("data-save-teams")) await action(() => api(`/events/${state.eventId}/teams`, "PUT",
    state.assignments.map(({ playerId, teamNumber, isGoalkeeper }) => ({ playerId, teamNumber, isGoalkeeper }))), "Team adjustments saved.");
  if (button.hasAttribute("data-reset-teams")) { selectGame(state.eventId); render(); notice("Unsaved adjustments discarded."); }
  if (button.hasAttribute("data-edit-attendance") && await confirmAction("Change attendance?", "Returning to setup discards the current team split. You can select attendance and regenerate teams.", "Return to setup")) {
    await action(() => api(`/events/${state.eventId}/teams`, "DELETE"), "Back in setup. Choose attendance and initialize again.");
  }
  if (button.hasAttribute("data-regenerate") && await confirmAction("Regenerate teams?", "Replace the current assignments with a new balanced split? Unsaved changes will be discarded.", "Regenerate"))
    await action(() => api(`/events/${state.eventId}/teams`, "POST", { teamCount: 2 }), "Teams regenerated.");
  if (button.hasAttribute("data-cancel") && await confirmAction("Cancel this game?", "Cancelled games are read-only and do not affect ratings.", "Cancel game"))
    await action(() => api(`/events/${state.eventId}/cancel`, "POST"), "Game cancelled.");
});

content.addEventListener("submit", async event => {
  event.preventDefault();
  const form = event.target;
  if (form.id === "create-game") await action(async () => {
    const e = await api("/events", "POST", { scheduledAt: new Date(form.elements.date.value).toISOString() });
    state.eventId = e.id;
  }, "Game created. Select who's playing.");
  if (form.id === "edit-game") await action(() => api(`/events/${state.eventId}`, "PUT",
    { scheduledAt: new Date(form.elements.date.value).toISOString() }), "Game date updated.");
  if (form.id === "result-form") {
    if (state.dirtyTeams) { notice("Save team adjustments before finishing the game.", true); return; }
    const scoreA = Number(form.elements.scoreA.value), scoreB = Number(form.elements.scoreB.value);
    if (await confirmAction("Finish this game?", `Team A ${scoreA} : ${scoreB} Team B. This locks the teams, attendance, and score and recalculates everyone's Elo.`, "Finish & lock"))
      await action(() => api(`/events/${state.eventId}/result`, "POST", { scoreA, scoreB }), "Game completed. Elo updated.");
  }
});

content.addEventListener("dragstart", event => {
  const player = event.target.closest("[data-player]");
  if (!player || !editable(currentGame()) || state.busy) { event.preventDefault(); return; }
  event.dataTransfer.setData("text/plain", player.dataset.player);
  event.dataTransfer.effectAllowed = "move";
  player.classList.add("dragging");
});
content.addEventListener("dragend", () => document.querySelectorAll(".dragging, .drag-over").forEach(el => el.classList.remove("dragging", "drag-over")));
content.addEventListener("dragover", event => {
  const team = event.target.closest("[data-team]");
  if (team && editable(currentGame()) && !state.busy) { event.preventDefault(); team.classList.add("drag-over"); }
});
content.addEventListener("dragleave", event => event.target.closest("[data-team]")?.classList.remove("drag-over"));
content.addEventListener("drop", event => {
  const team = event.target.closest("[data-team]");
  if (!team) return;
  event.preventDefault();
  team.classList.remove("drag-over");
  changeTeams(Number(event.dataTransfer.getData("text/plain")), Number(team.dataset.team));
});

document.querySelectorAll("[data-tab]").forEach(button => button.addEventListener("click", async () => {
  if (state.busy) return;
  if ((state.dirtyTeams || state.dirtyAttendance) && !await confirmAction("Leave unsaved changes?", "Your unsaved selections and adjustments will be discarded.", "Leave")) return;
  state.tab = button.dataset.tab; selectGame(null); notice(""); render();
}));
document.querySelectorAll("[data-close]").forEach(button => button.addEventListener("click", () => $(`#${button.dataset.close}`).close()));
$("#session-button").addEventListener("click", async () => {
  if (state.organizer) {
    if ((state.dirtyTeams || state.dirtyAttendance) && !await confirmAction("Sign out?", "Unsaved changes will be discarded.", "Sign out")) return;
    await action(async () => { await api("/auth/logout", "POST"); await session(); selectGame(state.eventId); render(); }, "Signed out.", false);
  } else { $("#login-form").reset(); $("#login-error").hidden = true; $("#login-dialog").showModal(); }
});

$("#login-form").addEventListener("submit", async event => {
  event.preventDefault();
  const button = $("#login-form button[type=submit]");
  button.disabled = true;
  try {
    await api("/auth/login", "POST", { password: event.target.elements.password.value });
    await session();
    $("#login-dialog").close();
    event.target.reset();
    await load();
    selectGame(state.eventId);
    render();
    notice("Welcome back. You're in organizer mode.");
  } catch (error) { $("#login-error").textContent = error.message; $("#login-error").hidden = false; }
  finally { button.disabled = false; }
});

$("#player-form").addEventListener("submit", async event => {
  event.preventDefault();
  const form = event.target;
  const button = $("button[type=submit]", form);
  button.disabled = true;
  try {
    const p = await api(state.editingPlayer ? `/players/${state.editingPlayer}` : "/players", state.editingPlayer ? "PUT" : "POST",
      { name: form.elements.name.value, surname: form.elements.surname.value, isGoalkeeper: form.elements.isGoalkeeper.checked });
    await load();
    if (state.addToEvent) {
      state.selected.add(p.id);
      if (p.isGoalkeeper && state.keepers.size < 2) state.keepers.add(p.id);
      state.dirtyAttendance = true;
    }
    $("#player-dialog").close();
    render();
    notice(state.addToEvent ? "Player added to the roster and selected for this game." : "Player saved.");
  } catch (error) { $("#player-error").textContent = error.message; $("#player-error").hidden = false; }
  finally { button.disabled = false; }
});

window.addEventListener("beforeunload", event => {
  if (state.dirtyTeams || state.dirtyAttendance) { event.preventDefault(); event.returnValue = ""; }
});

try {
  await session();
  await load();
  render();
} catch (error) {
  content.innerHTML = '<div class="empty"><h2>Unable to load games</h2><p>Check your connection and try again.</p><button class="button subtle" type="button" id="retry">Retry</button></div>';
  notice(error.message, true);
  $("#retry").addEventListener("click", () => location.reload());
  content.setAttribute("aria-busy", "false");
}
