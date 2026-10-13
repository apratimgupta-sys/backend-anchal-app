FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet publish src/Anchal.Api/Anchal.Api.csproj --configuration Release --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8090
EXPOSE 8090

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Anchal.Api.dll"]
