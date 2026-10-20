# Built from the solution folder: docker build -f Shopfront.Worker/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Packages.props ./
COPY Shopfront.Core/Shopfront.Core.csproj Shopfront.Core/
COPY Shopfront.Worker/Shopfront.Worker.csproj Shopfront.Worker/
RUN dotnet restore Shopfront.Worker/Shopfront.Worker.csproj

COPY Shopfront.Core/ Shopfront.Core/
COPY Shopfront.Worker/ Shopfront.Worker/
COPY machines/ machines/
RUN dotnet publish Shopfront.Worker/Shopfront.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Shopfront.Worker.dll"]
