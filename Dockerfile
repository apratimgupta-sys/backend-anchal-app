FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet publish src/Anchal.Api/Anchal.Api.csproj --configuration Release --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

EXPOSE 8090

COPY --from=build /app/publish .
# Supabase's public CA is required when Commissioner Monitoring uses
# `SSL Mode=VerifyFull`.  Keep it in the image rather than depending on an
# environment-specific local filesystem path.
COPY deploy/supabase-ca.crt /etc/ssl/certs/supabase-ca.crt
# Render injects its own ASPNETCORE_URLS value, which can bind only loopback.
# Pass the public container binding as a command-line setting so it wins over
# that environment value while retaining the local 8090 default.
ENTRYPOINT ["sh", "-c", "exec dotnet Anchal.Api.dll --urls \"http://0.0.0.0:${PORT:-8090}\""]
