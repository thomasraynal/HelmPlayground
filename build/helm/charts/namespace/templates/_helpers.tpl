{{- define "namespace" -}}
{{- .Values.group | replace "." "-" -}}
{{- end -}}