FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Shared.Domain/Shared.Domain.csproj",       "src/Shared.Domain/"]
COPY ["src/CustomerPortal/CustomerPortal.csproj",     "src/CustomerPortal/"]
RUN dotnet restore "src/CustomerPortal/CustomerPortal.csproj"
COPY src/Shared.Domain/   src/Shared.Domain/
COPY src/CustomerPortal/  src/CustomerPortal/
WORKDIR "/src/src/CustomerPortal"
RUN dotnet build "CustomerPortal.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "CustomerPortal.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "CustomerPortal.dll"]
