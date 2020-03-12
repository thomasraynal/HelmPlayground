{{- define "api-chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "namespace" -}}
{{- .Values.group | replace "." "-" -}}
{{- end -}}
