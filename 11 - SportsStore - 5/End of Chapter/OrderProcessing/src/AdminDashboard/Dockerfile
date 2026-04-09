FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Shared.Domain/Shared.Domain.csproj",         "src/Shared.Domain/"]
COPY ["src/AdminDashboard/AdminDashboard.csproj",       "src/AdminDashboard/"]
RUN dotnet restore "src/AdminDashboard/AdminDashboard.csproj"
COPY src/Shared.Domain/   src/Shared.Domain/
COPY src/AdminDashboard/  src/AdminDashboard/
WORKDIR "/src/src/AdminDashboard"
RUN dotnet build "AdminDashboard.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AdminDashboard.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "AdminDashboard.dll"]
