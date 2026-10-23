FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2
RUN apt-get update && apt-get install -y git
# Matches the Microsoft.EntityFrameworkCore pin in Directory.Packages.props.
RUN dotnet tool install --global dotnet-ef --version 10.0.12
ENV PATH="$PATH:/root/.dotnet/tools"
