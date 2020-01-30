{{/*
Expand the name of the chart.
*/}}
{{- define "bz-api.name" -}}
{{- .Values.app | replace "." "-" -}}
{{- end -}}

{{- define "bz-api.fullname" -}}
{{- .Values.app | replace "." "-" -}}
{{- end -}}

{{/*
Create chart name and version as used by the chart label.
*/}}
{{- define "bz-api.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" -}}
{{- end -}}
