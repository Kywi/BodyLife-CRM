#!/usr/bin/env python3
"""Manage the repository-scoped single-writer lease for Codex tasks."""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from pathlib import Path


TOKEN_PATTERN = re.compile(r"^[A-Za-z0-9._-]{1,128}$")


class LeaseError(RuntimeError):
    pass


def validate_token(value: str, label: str) -> str:
    if not TOKEN_PATTERN.fullmatch(value):
        raise LeaseError(
            f"{label} must use 1-128 letters, digits, dot, underscore, or hyphen characters"
        )
    return value


def find_repo_root(start: Path) -> Path:
    for candidate in (start, *start.parents):
        if (candidate / ".git").is_dir():
            return candidate
    raise LeaseError("run inside a Git repository with a .git directory")


class WriteLease:
    def __init__(self, repo_root: Path) -> None:
        self.lease_dir = repo_root / ".git" / "codex-write-lease"
        self.state_path = self.lease_dir / "lease.json"

    def acquire(self, owner: str) -> None:
        try:
            self.lease_dir.mkdir()
        except FileExistsError as exc:
            raise LeaseError(
                f"write lease already exists at {self.lease_dir}; inspect status and active tasks"
            ) from exc

        try:
            self._write({"owner": owner, "writer": None})
        except Exception:
            self.lease_dir.rmdir()
            raise

    def grant(self, owner: str, writer: str) -> None:
        state = self._owned_state(owner)
        current = state["writer"]
        if current not in (None, writer):
            raise LeaseError(f"lease is already granted to {current}")
        state["writer"] = writer
        self._write(state)

    def check(self, owner: str, writer: str) -> None:
        state = self._owned_state(owner)
        if state["writer"] != writer:
            raise LeaseError(
                f"writer grant mismatch: expected {writer}, actual {state['writer']}"
            )

    def revoke(self, owner: str, writer: str) -> None:
        state = self._owned_state(owner)
        if state["writer"] != writer:
            raise LeaseError(
                f"cannot revoke {writer}: active writer is {state['writer']}"
            )
        state["writer"] = None
        self._write(state)

    def release(self, owner: str) -> None:
        state = self._owned_state(owner)
        if state["writer"] is not None:
            raise LeaseError(f"revoke active writer {state['writer']} before release")
        self.state_path.unlink()
        self.lease_dir.rmdir()

    def status(self) -> dict[str, str | None]:
        return self._read()

    def _owned_state(self, owner: str) -> dict[str, str | None]:
        state = self._read()
        if state["owner"] != owner:
            raise LeaseError(
                f"owner mismatch: expected {owner}, actual {state['owner']}"
            )
        return state

    def _read(self) -> dict[str, str | None]:
        if not self.lease_dir.is_dir() or not self.state_path.is_file():
            raise LeaseError(f"valid write lease not found at {self.lease_dir}")
        try:
            state = json.loads(self.state_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            raise LeaseError(f"write lease metadata is unreadable: {exc}") from exc

        if set(state) != {"owner", "writer"}:
            raise LeaseError("write lease metadata has an unexpected shape")
        if not isinstance(state["owner"], str):
            raise LeaseError("stored owner must be a string")
        if state["writer"] is not None and not isinstance(state["writer"], str):
            raise LeaseError("stored writer must be a string or null")
        validate_token(state["owner"], "stored owner")
        if state["writer"] is not None:
            validate_token(state["writer"], "stored writer")
        return state

    def _write(self, state: dict[str, str | None]) -> None:
        temp_path = self.lease_dir / f"lease.{os.getpid()}.tmp"
        try:
            with temp_path.open("x", encoding="utf-8", newline="\n") as stream:
                json.dump(state, stream, sort_keys=True)
                stream.write("\n")
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temp_path, self.state_path)
        finally:
            temp_path.unlink(missing_ok=True)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--repo",
        type=Path,
        default=Path.cwd(),
        help="repository path or a directory beneath it (default: current directory)",
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    for command in ("acquire", "release"):
        subparser = subparsers.add_parser(command)
        subparser.add_argument("--owner", required=True)

    for command in ("grant", "check", "revoke"):
        subparser = subparsers.add_parser(command)
        subparser.add_argument("--owner", required=True)
        subparser.add_argument("--writer", required=True)

    subparsers.add_parser("status")
    return parser


def main() -> int:
    args = build_parser().parse_args()
    try:
        repo_root = find_repo_root(args.repo.resolve())
        lease = WriteLease(repo_root)
        owner = (
            validate_token(args.owner, "owner")
            if hasattr(args, "owner")
            else None
        )
        writer = (
            validate_token(args.writer, "writer")
            if hasattr(args, "writer")
            else None
        )

        if args.command == "acquire":
            lease.acquire(owner)
        elif args.command == "grant":
            lease.grant(owner, writer)
        elif args.command == "check":
            lease.check(owner, writer)
        elif args.command == "revoke":
            lease.revoke(owner, writer)
        elif args.command == "release":
            lease.release(owner)
        else:
            print(json.dumps(lease.status(), sort_keys=True))
            return 0

        print(f"lease {args.command}: ok")
        return 0
    except LeaseError as exc:
        print(f"lease {args.command}: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
