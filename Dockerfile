FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Scarab.csproj .
COPY Scarab.ServiceDefaults/Scarab.ServiceDefaults.csproj Scarab.ServiceDefaults/
RUN dotnet restore
COPY . .
RUN dotnet publish Scarab.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 6060
ENTRYPOINT ["dotnet", "Scarab.dll"]
