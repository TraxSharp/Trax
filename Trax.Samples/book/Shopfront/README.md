# Shopfront

The shop [the Trax book](https://traxsharp.net/docs/book) builds, as it stands at the end of the last
chapter: a hub, two workers, a React storefront over a generated client, and the book's test project.
Read the book to see how it got here; read this to see where it ends up.

Every file the book prints is here exactly as printed, and every other file is one a command in the book
generates (the solution, the tool manifest, the machine's corpus, the migration, the generated client,
the lockfiles). `BookSampleIsTheBookTests` in `Trax.Docs` fails when a page and this folder disagree.

Two files are not the book's. `Directory.Build.props` keeps `Trax.Samples`' build settings out, and
`Directory.Build.targets` swaps the book's Trax `PackageReference`s for project references, so the sample
builds against the code in the same commit. Copied out of the repository, the folder restores the
packages from nuget.org instead.

## Run it

As the book does, from this folder, with Postgres and RabbitMQ on their default ports:

```bash
docker run -d --name shopfront-postgres -p 5432:5432 \
  -e POSTGRES_PASSWORD=shopfront -e POSTGRES_DB=shopfront postgres:17
# guest is let in from loopback only, and a published port does not arrive on loopback
docker run -d --name shopfront-rabbitmq -p 5672:5672 \
  -e RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS='-rabbit loopback_users []' rabbitmq:4
dotnet run --project Shopfront.Hub --urls http://localhost:5080
dotnet run --project Shopfront.Worker --urls http://localhost:5081
cd web && npm install && npm run dev
```

## Test it

```bash
dotnet test Shopfront.slnx
cd web && npm test
```

CI runs both in the `book-sample` job, against the same Postgres and RabbitMQ users the book uses.

## Contributing

Change the book first, then this folder to match. A chapter that changes a generated file says which
command regenerates it; run it here the same way.

## Licence

MIT, as the rest of Trax.
