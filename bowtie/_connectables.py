"""
Connectables implement a mini-language for connecting to supported harnesses.

They allow connecting to various kinds of harnesses over different
"connectors", for example:

    * in a container which Bowtie manages (starts and stops)
    * in a pre-existing / externally managed container
    * in-memory within Bowtie itself, with no IPC (not yet implemented)

The general form of a connectable (also known as a *connectable ID*,
*connectable string* or *connectable description*) is:

    [<connector>:]<id>[:<arguments>*]

where the connector indicates how to connect to the harness.
Currently supported connectors are:

    * ``image``: a container image which Bowtie will start, stop and delete
                 which must speak Bowtie's harness protocol
    * ``container``: an external running container which Bowtie will connect to
                 which must speak Bowtie's harness protocol

If no connector is specified, ``image`` is assumed.

The ``id`` is a connector-specific identifier and should indicate
the specific intended implementation. For example, for container images, it
must be the name of a container image which will be pulled if needed.
It need not be fully qualified (i.e. include the repository),
and will default to pulling from Bowtie's own image repository.

As concrete examples:

    * ``image:example`` refers to an implemenetation image named `"example"`
      (which will be retrieved from Bowtie's container registry if not present)
    * ``example``, with no ``image`` connector, refers to the same image above
    * ``image:foo/bar:latest`` is an image with fully specified OCI container
      registry
    * ``container:deadbeef`` refers to an OCI container with ID ``deadbeef``
      which is assumed to be running (and will be attached to)

Connectables are loosely inspired by `Twisted's strports
<twisted.internet.endpoints.clientFromString`.
"""

from __future__ import annotations

from contextlib import asynccontextmanager
from typing import TYPE_CHECKING, Any, Protocol

from attrs import field, frozen
from click.shell_completion import CompletionItem
from rpds import HashTrieMap
import click

from bowtie import _containers
from bowtie._core import Implementation

if TYPE_CHECKING:
    from collections.abc import AsyncIterator
    from contextlib import AbstractAsyncContextManager

    from bowtie._core import Connection


class UnknownConnector(Exception):
    """
    The connector provided does not exist.
    """


class Connector(Protocol):

    #: The string name of this connector type.
    connector: str

    def connect(self) -> AbstractAsyncContextManager[Connection]: ...


#: A string identifying an implementation supporting Bowtie's harness protocol.
#: Connectable IDs should be unique within a run or report.
ConnectableId = str


@frozen
class Connectable:

    _id: ConnectableId = field(alias="id", repr=False)
    _connector: Connector = field(alias="connector")

    _connectors = HashTrieMap(
        (cls.connector, cls)
        for cls in [
            _containers.ConnectableImage,
            _containers.ConnectableContainer,
        ]
    )

    @classmethod
    def from_str(cls, fqid: ConnectableId):
        connector, sep, id = fqid.partition(":")
        if not sep:
            connector, id = "image", connector
        Connector = cls._connectors.get(connector)
        if Connector is None:
            if "/" in connector:
                return cls(id=fqid, connector=cls._connectors["image"](fqid))
            raise UnknownConnector(connector)
        return cls(id=fqid, connector=Connector(id))

    @asynccontextmanager
    async def connect(self, **kwargs: Any) -> AsyncIterator[Implementation]:
        async with (
            self._connector.connect() as connection,
            Implementation.start(
                id=self._id,
                connection=connection,
                **kwargs,
            ) as implementation,
        ):
            yield implementation

    def to_terse(self) -> ConnectableId:
        """
        The tersest connectable description someone could write.
        """
        return self._id.removeprefix("image:").removeprefix(
            f"{_containers.IMAGE_REPOSITORY}/",
        )


class ClickParam(click.ParamType):
    """
    Select a supported Bowtie implementation by its connectable string.
    """

    name = "implementation"

    def convert(
        self,
        value: str,
        param: click.Parameter | None,
        ctx: click.Context | None,
    ) -> Connectable:
        return Connectable.from_str(value)

    def shell_complete(
        self,
        ctx: click.Context,
        param: click.Parameter,
        incomplete: str,
    ) -> list[CompletionItem]:
        # FIXME: All supported Connectable values
        return [
            CompletionItem(name)
            for name in Implementation.known()
            if name.startswith(incomplete.lower())
        ]
