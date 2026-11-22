# GameHub

ASP.NET Core demo game catalogue with SQLite persistence, password hashing, cookie authentication, user libraries and a role-protected catalogue API. The seed games are fictional and library additions are simulations, not purchases.

## Run

Install .NET 8 SDK and run `dotnet run --urls http://127.0.0.1:5000`, then open the URL. Optional administrator account: set `ADMIN_EMAIL` and `ADMIN_PASSWORD` (12+ characters) as shell environment variables before first startup. Do not commit secrets. `.env.example` documents variables; .NET does not load it automatically. Administrator creation affects the database on startup.

Local demo; configure HTTPS, rate limiting, migrations and deployment secrets before hosting publicly.

CI builds the app and runs API smoke checks.
