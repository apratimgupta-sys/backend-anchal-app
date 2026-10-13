module.exports = {
  apps: [{
    name: "anchal-api",
    script: "Anchal.Api.dll",
    interpreter: "dotnet",
    cwd: process.env.ANCHAL_RELEASE_DIR,
    env: {
      ASPNETCORE_ENVIRONMENT: "Production",
      ASPNETCORE_URLS: "http://127.0.0.1:8090"
    }
  }]
};
