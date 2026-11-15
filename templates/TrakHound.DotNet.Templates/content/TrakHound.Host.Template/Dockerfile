# Builds a container image of the host. The host references the package project in the folder next to it,
# so build from the parent folder: docker build -f UserHostId/Dockerfile -t userhostid .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY UserModuleId/ UserModuleId/
COPY UserHostId/ UserHostId/
# Leave out local build output (it has the paths of the machine it was built on)
RUN find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
RUN dotnet publish UserHostId/UserHostId.csproj -c Release -o /app

#if (isApi)
FROM mcr.microsoft.com/dotnet/aspnet:10.0
#else
FROM mcr.microsoft.com/dotnet/runtime:10.0
#endif
WORKDIR /app
COPY --from=build /app .
#if (isApi)
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
#endif
# Set the settings with environment variables, ex. -e TrakHound__InstanceUrl=https://instance.example.com
ENTRYPOINT ["dotnet", "UserHostId.dll"]
