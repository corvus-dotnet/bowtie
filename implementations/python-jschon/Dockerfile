FROM python:3.12.6-alpine
WORKDIR /usr/src/myapp
RUN python3 -m pip install jschon
COPY bowtie_jschon.py .
CMD ["python3", "bowtie_jschon.py"]
