# WITS.UI

Blazor Web App (interactive server) for WITS. Plain hand-written CSS in
`wwwroot/app.css` and the scoped component stylesheets; no CSS framework.

## Run

Start the API first (see the repository README), then:

```sh
dotnet run --project src/WITS.UI --launch-profile http
```

The UI listens on `http://localhost:5014` and calls the API at the `ApiBaseUrl`
setting (`http://localhost:5351` by default in `appsettings.json`).

## Pages

- `/login` — signs in against `POST api/v1/auth/login`.
- `/` — dashboard.
- `/tickets` — paged ticket list (`GET api/v1/ticket`).
- `/tickets/{id}` — ticket detail (`GET api/v1/ticket/{id}`).

## Known limits

- The access token lives in the Blazor circuit (scoped `AuthSession`), so a
  browser refresh signs the user out. Add protected session storage when that
  becomes annoying.
- Ticket pages surface API errors as-is. The API's `TicketService` is still
  unimplemented, so ticket requests currently return `500`; the UI shows the
  problem detail and trace id.
- No registration, ticket create/edit, or role-based UI yet.
