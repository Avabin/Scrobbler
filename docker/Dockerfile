# Build image for the scrobbler GitHub Actions workflows.
# Provides Homebrew (Linuxbrew), a native toolchain (clang) for NativeAOT,
# and the .NET SDK pinned to global.json.
#
# Multi-stage: the "prep" stage installs Homebrew + .NET and trims bloat,
# the final stage copies only what's needed, keeping the image small.
#
# Build & publish:
#   docker build -f docker/Dockerfile -t ghcr.io/avabin/scrobbler-build:latest .

############## Stage 1: prepare Homebrew + .NET ##############
FROM fedora:44 AS prep

ENV LANG=C.UTF-8 \
    LC_ALL=C.UTF-8 \
    CI=1

# Base tooling needed to bootstrap brew + dotnet and (during prep) to validate
# the toolchain. The final stage re-installs only the runtime subset.
RUN dnf install -y --setopt=install_weak_deps=False \
        brotli-devel \
        clang \
        curl \
        file \
        findutils \
        git \
        libicu-devel \
        make \
        procps-ng \
        sudo \
        which \
        zlib-devel \
    && dnf clean all

RUN useradd -m -s /bin/bash linuxbrew \
    && echo 'linuxbrew ALL=(ALL) NOPASSWD:ALL' >> /etc/sudoers.d/linuxbrew \
    && chmod 0440 /etc/sudoers.d/linuxbrew

ENV HOMEBREW_PREFIX=/home/linuxbrew/.linuxbrew \
    HOMEBREW_CELLAR=/home/linuxbrew/.linuxbrew/Cellar \
    HOMEBREW_REPOSITORY=/home/linuxbrew/.linuxbrew/Homebrew \
    PATH=/home/linuxbrew/.linuxbrew/bin:/home/linuxbrew/.linuxbrew/sbin:$PATH \
    HOMEBREW_NO_AUTO_UPDATE=1 \
    HOMEBREW_NO_INSTALL_CLEANUP=1 \
    HOMEBREW_NO_ANALYTICS=1

# --- Homebrew (as linuxbrew; brew refuses to run as root) ---
RUN git clone --depth=1 https://github.com/Homebrew/brew "$HOMEBREW_REPOSITORY" \
    && mkdir -p "$HOMEBREW_PREFIX/bin" "$HOMEBREW_PREFIX/sbin" \
    && ln -sfn "$HOMEBREW_REPOSITORY/bin/brew" "$HOMEBREW_PREFIX/bin/brew" \
    && mkdir -p "$HOMEBREW_PREFIX/lib" "$HOMEBREW_PREFIX/share" "$HOMEBREW_PREFIX/etc" "$HOMEBREW_PREFIX/Cellar" "$HOMEBREW_PREFIX/Caskroom" \
    && ln -sfn "$HOMEBREW_REPOSITORY/Library/Homebrew/vendor/portable-ruby/current/bin/ruby" "$HOMEBREW_PREFIX/bin/ruby" \
    && "$HOMEBREW_PREFIX/bin/brew" --version

USER linuxbrew
WORKDIR /home/linuxbrew

ENV DOTNET_ROOT=/home/linuxbrew/.dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    NUGET_PACKAGES=/home/linuxbrew/.nuget/packages \
    PATH=/home/linuxbrew/.dotnet:$PATH

# --- .NET SDK (matches global.json: 10.0.302) ---
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --channel 10.0 --install-dir "$DOTNET_ROOT" \
    && rm /tmp/dotnet-install.sh

# Trim unused SDK bloat for a console app: drop ASP.NET Core shared runtime,
# its reference pack, F#, templates and the wrong-target host pack.
RUN rm -rf \
        "$DOTNET_ROOT/shared/Microsoft.AspNetCore.App" \
        "$DOTNET_ROOT/packs/Microsoft.AspNetCore.App.Ref" \
        "$DOTNET_ROOT/templates" \
    && find "$DOTNET_ROOT/sdk" -maxdepth 2 -type d -name 'FSharp' -exec rm -rf {} + \
    && find "$DOTNET_ROOT" -name "*.pdb" -type f -delete \
    && dotnet --version

############## Stage 2: minimal runtime image ##############
FROM fedora:44 AS final

ENV LANG=C.UTF-8 \
    LC_ALL=C.UTF-8 \
    CI=1

# Runtime subset: git/curl/sudo/etc. for actions, clang for NativeAOT, and the
# runtime libs dotnet + brew need. No gcc, no dev headers, no PGO data.
RUN dnf install -y --setopt=install_weak_deps=False \
        brotli \
        clang \
        curl \
        file \
        findutils \
        git \
        libicu \
        make \
        nodejs \
        procps-ng \
        sudo \
        which \
        zlib \
    && dnf clean all \
    && rm -rf /usr/share/llvm-*.profdata \
    && rm -rf /usr/share/doc /usr/share/man /usr/share/info \
    && find /usr/share/locale -mindepth 1 -maxdepth 1 ! -name 'C.utf8' ! -name 'en*' ! -name 'locale-archive' -exec rm -rf {} + \
    && clang --version

RUN useradd -m -s /bin/bash linuxbrew \
    && echo 'linuxbrew ALL=(ALL) NOPASSWD:ALL' >> /etc/sudoers.d/linuxbrew \
    && chmod 0440 /etc/sudoers.d/linuxbrew

# Ensure brew/dotnet stay on PATH and env is intact even after `su - linuxbrew`
# (which resets the inherited environment). Sourced by every login shell.
RUN printf '%s\n' \
        'export HOMEBREW_PREFIX=/home/linuxbrew/.linuxbrew' \
        'export HOMEBREW_CELLAR=/home/linuxbrew/.linuxbrew/Cellar' \
        'export HOMEBREW_REPOSITORY=/home/linuxbrew/.linuxbrew/Homebrew' \
        'export HOMEBREW_NO_AUTO_UPDATE=1' \
        'export HOMEBREW_NO_INSTALL_CLEANUP=1' \
        'export HOMEBREW_NO_ANALYTICS=1' \
        'export DOTNET_ROOT=/home/linuxbrew/.dotnet' \
        'export DOTNET_CLI_TELEMETRY_OPTOUT=1' \
        'export DOTNET_NOLOGO=1' \
        'export NUGET_PACKAGES=/home/linuxbrew/.nuget/packages' \
        'export PATH=/home/linuxbrew/.nuget/packages/.bin:/home/linuxbrew/.dotnet:/home/linuxbrew/.linuxbrew/bin:/home/linuxbrew/.linuxbrew/sbin:$PATH' \
        > /etc/profile.d/scrobbler-build.sh \
    && chmod 0644 /etc/profile.d/scrobbler-build.sh

USER linuxbrew
WORKDIR /home/linuxbrew

ENV HOMEBREW_PREFIX=/home/linuxbrew/.linuxbrew \
    HOMEBREW_CELLAR=/home/linuxbrew/.linuxbrew/Cellar \
    HOMEBREW_REPOSITORY=/home/linuxbrew/.linuxbrew/Homebrew \
    HOMEBREW_NO_AUTO_UPDATE=1 \
    HOMEBREW_NO_INSTALL_CLEANUP=1 \
    HOMEBREW_NO_ANALYTICS=1 \
    DOTNET_ROOT=/home/linuxbrew/.dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    NUGET_PACKAGES=/home/linuxbrew/.nuget/packages \
    PATH=/home/linuxbrew/.nuget/packages/.bin:/home/linuxbrew/.dotnet:/home/linuxbrew/.linuxbrew/bin:/home/linuxbrew/.linuxbrew/sbin:$PATH

# Copy the prepared Homebrew + .NET from the prep stage, preserving ownership.
COPY --from=prep --chown=linuxbrew:linuxbrew /home/linuxbrew/.linuxbrew /home/linuxbrew/.linuxbrew
COPY --from=prep --chown=linuxbrew:linuxbrew /home/linuxbrew/.dotnet /home/linuxbrew/.dotnet

# Sanity-check the copied toolchain (as linuxbrew) before switching back to root.
RUN dotnet --version && "$HOMEBREW_PREFIX/bin/brew" --version

# The GitHub Actions runner must run as root so it can write its own state
# (e.g. /__w/_temp). Only the brew step drops to `linuxbrew` at run time.
USER root
WORKDIR /workspace
