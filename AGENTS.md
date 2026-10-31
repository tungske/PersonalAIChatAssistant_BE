## Project Overview
- Backend: ASP.NET Core Web API
- Architecture: Clean Architecture
- Database: SQL Server
- Cache: Redis

## Architecture Rules
- Domain must not depend on other layers.
- Application contains business use cases and interfaces.
- Infrastructure implements persistence and external integrations.
- WebAPI handles HTTP requests and dependency injection.
- Follow the existing project structure.

## Coding Standards
- Use async/await for asynchronous I/O.
- Pass CancellationToken where appropriate.
- Follow SOLID principles.
- Use dependency injection.
- Validate inputs and handle errors consistently.
- Never hardcode secrets or credentials.

## Testing
- Write unit tests for business logic.
- Add integration tests for critical API flows.
- Run relevant tests after modifying code.

## Workflow
- Inspect existing code before making changes.
- Prefer minimal, focused changes.
- Explain important design decisions.
- Do not introduce new dependencies without justification.
