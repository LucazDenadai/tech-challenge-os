FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/OficinaMecanica.OS.Domain/OficinaMecanica.OS.Domain.csproj src/OficinaMecanica.OS.Domain/
COPY src/OficinaMecanica.OS.Application/OficinaMecanica.OS.Application.csproj src/OficinaMecanica.OS.Application/
COPY src/OficinaMecanica.OS.Infrastructure/OficinaMecanica.OS.Infrastructure.csproj src/OficinaMecanica.OS.Infrastructure/
COPY src/OficinaMecanica.OS.API/OficinaMecanica.OS.API.csproj src/OficinaMecanica.OS.API/

RUN dotnet restore src/OficinaMecanica.OS.API/OficinaMecanica.OS.API.csproj

COPY src/ src/

RUN dotnet publish src/OficinaMecanica.OS.API/OficinaMecanica.OS.API.csproj \
    -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build --chown=app:app --chmod=755 /app/publish .
USER app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "OficinaMecanica.OS.API.dll"]
