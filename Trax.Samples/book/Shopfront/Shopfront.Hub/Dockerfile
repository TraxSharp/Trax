# Built from the solution folder: docker build -f Shopfront.Hub/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# The packages first, so a change to the code does not restore them again.
COPY Directory.Packages.props ./
COPY Shopfront.Core/Shopfront.Core.csproj Shopfront.Core/
COPY Shopfront.Hub/Shopfront.Hub.csproj Shopfront.Hub/
RUN dotnet restore Shopfront.Hub/Shopfront.Hub.csproj

COPY Shopfront.Core/ Shopfront.Core/
COPY Shopfront.Hub/ Shopfront.Hub/
# The machine's corpus, which the library embeds, and the storefront's documents, which the hub
# registers at start.
COPY machines/ machines/
COPY web/src/trax/trax.documents.json web/src/trax/
RUN dotnet publish Shopfront.Hub/Shopfront.Hub.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Shopfront.Hub.dll"]
