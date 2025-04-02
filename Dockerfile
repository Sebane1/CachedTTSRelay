FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /source

COPY --link . .
RUN dotnet restore -a $TARGETARCH *.sln
RUN dotnet publish -a $TARGETARCH --no-restore -o /out

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/runtime:8.0
COPY --link --from=build /out /out
WORKDIR /app
EXPOSE 5670
CMD ["/out/CachedTTSRelay"]
