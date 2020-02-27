{{- define "namespace" -}}
{{- .Values.group | replace "." "-" -}}-namespace
{{- end -}}