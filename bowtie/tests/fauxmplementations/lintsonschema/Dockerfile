FROM python:3.12.6-alpine
RUN python -m pip install jsonschema 'referencing-loaders>=2024.5.2'
COPY lintsonschema /usr/local/bin
COPY schemas /schemas
CMD ["/usr/local/bin/lintsonschema"]
