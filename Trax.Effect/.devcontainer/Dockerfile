FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:83e0db97c45d2e39b80123fe42940a23c423405a17f80b608a4b8768033d6392
RUN apt-get update && apt-get install -y git
# Matches the Microsoft.EntityFrameworkCore pin in Directory.Packages.props.
RUN dotnet tool install --global dotnet-ef --version 10.0.12
ENV PATH="$PATH:/root/.dotnet/tools"
