# ---- build -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY SchoolAdmission.Api/SchoolAdmission.Api.csproj SchoolAdmission.Api/
RUN dotnet restore SchoolAdmission.Api/SchoolAdmission.Api.csproj
COPY SchoolAdmission.Api/ SchoolAdmission.Api/
RUN dotnet publish SchoolAdmission.Api/SchoolAdmission.Api.csproj -c Release -o /app/publish --no-restore

# ---- tests (only built when asked: docker compose --profile test run --rm backend-tests) ----
FROM build AS test
COPY SchoolAdmission.Tests/ SchoolAdmission.Tests/
RUN dotnet restore SchoolAdmission.Tests/SchoolAdmission.Tests.csproj
WORKDIR /src/SchoolAdmission.Tests
ENTRYPOINT ["dotnet", "test", "--no-restore", "--logger", "console;verbosity=normal"]

# ---- runtime (last stage = default build target) -------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SchoolAdmission.Api.dll"]
