#!/usr/bin/env python3
"""Generates timber-yard-load.jmx (the plan is XML, so it is generated rather than hand-edited).
   python3 build_plan.py  ->  rewrites timber-yard-load.jmx next to this file.
Everything that varies between smoke / load / stress / spike / soak is a JMeter property
(see profiles/*.properties), so there is ONE plan for all scenarios."""
import os
from xml.sax.saxutils import escape

HOST = '${__P(host,localhost)}'
PORTS = {'auth': '${__P(auth.port,5030)}', 'logintake': '${__P(logintake.port,5201)}',
         'sawmill': '${__P(sawmill.port,5210)}', 'treatment': '${__P(treatment.port,5000)}'}
TOK_SUP, TOK_MGR = '${__P(TOKEN_SUP)}', '${__P(TOKEN_MGR)}'
WAVES = 5
_n = [0]


def uid():
    _n[0] += 1
    return _n[0]


def hdr(name, headers):
    items = ''.join(
        f'<elementProp name="{k}" elementType="Header"><stringProp name="Header.name">{k}</stringProp>'
        f'<stringProp name="Header.value">{escape(v)}</stringProp></elementProp>' for k, v in headers)
    return (f'<HeaderManager guiclass="HeaderPanel" testclass="HeaderManager" testname="{name}" enabled="true">'
            f'<collectionProp name="HeaderManager.headers">{items}</collectionProp></HeaderManager><hashTree/>')


def resp_assert(name, field, value, ignore_status=False, contains=False):
    return (f'<ResponseAssertion guiclass="AssertionGui" testclass="ResponseAssertion" testname="{escape(name)}" enabled="true">'
            f'<collectionProp name="Asserion.test_strings"><stringProp name="{uid()}">{escape(value)}</stringProp></collectionProp>'
            f'<stringProp name="Assertion.custom_message"></stringProp>'
            f'<stringProp name="Assertion.test_field">{field}</stringProp>'
            f'<boolProp name="Assertion.assume_success">{"true" if ignore_status else "false"}</boolProp>'
            f'<intProp name="Assertion.test_type">{2 if contains else 8}</intProp></ResponseAssertion><hashTree/>')


def sampler(kind, label, svc, path, method='GET', expect=200, body=None, token=None, contains=None, extract=None):
    """kind: read|report|write|login  (becomes the '[kind] ' label prefix used by check_thresholds.py)"""
    name = escape(f'[{kind}] {label}')
    if body is None:
        args = '<collectionProp name="Arguments.arguments"/>'
        raw = 'false'
    else:
        args = ('<collectionProp name="Arguments.arguments"><elementProp name="" elementType="HTTPArgument">'
                '<boolProp name="HTTPArgument.always_encode">false</boolProp>'
                f'<stringProp name="Argument.value">{escape(body)}</stringProp>'
                '<stringProp name="Argument.metadata">=</stringProp></elementProp></collectionProp>')
        raw = 'true'
    kids = ''
    if token:
        kids += hdr('Auth override', [('Authorization', f'Bearer {token}')])
    kids += resp_assert(f'Status is {expect}', 'Assertion.response_code', str(expect), ignore_status=(expect >= 400))
    if contains:
        kids += resp_assert(f'Body contains {contains}', 'Assertion.response_data', contains, contains=True)
    if extract:  # pull a JSON field into a thread variable, defaulting to 1
        kids += (f'<JSONPostProcessor guiclass="JSONPostProcessorGui" testclass="JSONPostProcessor" testname="Extract {extract[0]}" enabled="true">'
                 f'<stringProp name="JSONPostProcessor.referenceNames">{extract[0]}</stringProp>'
                 f'<stringProp name="JSONPostProcessor.jsonPathExprs">{extract[1]}</stringProp>'
                 '<stringProp name="JSONPostProcessor.match_numbers">1</stringProp>'
                 '<stringProp name="JSONPostProcessor.defaultValues">1</stringProp></JSONPostProcessor><hashTree/>')
    return (f'<HTTPSamplerProxy guiclass="HttpTestSampleGui" testclass="HTTPSamplerProxy" testname="{name}" enabled="true">'
            f'<elementProp name="HTTPsampler.Arguments" elementType="Arguments" guiclass="HTTPArgumentsPanel" testclass="Arguments" enabled="true">{args}</elementProp>'
            f'<stringProp name="HTTPSampler.domain">{HOST}</stringProp><stringProp name="HTTPSampler.port">{PORTS[svc]}</stringProp>'
            f'<stringProp name="HTTPSampler.protocol">http</stringProp><stringProp name="HTTPSampler.contentEncoding">UTF-8</stringProp>'
            f'<stringProp name="HTTPSampler.path">{escape(path)}</stringProp><stringProp name="HTTPSampler.method">{method}</stringProp>'
            '<boolProp name="HTTPSampler.follow_redirects">true</boolProp><boolProp name="HTTPSampler.auto_redirects">false</boolProp>'
            f'<boolProp name="HTTPSampler.use_keepalive">true</boolProp><boolProp name="HTTPSampler.postBodyRaw">{raw}</boolProp>'
            '<stringProp name="HTTPSampler.connect_timeout">5000</stringProp><stringProp name="HTTPSampler.response_timeout">30000</stringProp>'
            f'</HTTPSamplerProxy><hashTree>{kids}</hashTree>')


def controller(name, children):
    return (f'<GenericController guiclass="LogicControllerGui" testclass="GenericController" testname="{name}" enabled="true"/>'
            f'<hashTree>{"".join(children)}</hashTree>')


FROM, TO = '${__timeShift(yyyy-MM-dd,,-P365D,,)}', '${__time(yyyy-MM-dd,)}'

BROWSE = [
    sampler('read', 'LogIntake stock', 'logintake', '/api/LogIntake/stock'),
    sampler('read', 'LogIntake species', 'logintake', '/api/LogIntake/species'),
    sampler('read', 'LogIntake log-lengths', 'logintake', '/api/LogIntake/log-lengths'),
    sampler('read', 'LogIntake suppliers', 'logintake', '/api/LogIntake/suppliers'),
    sampler('read', 'LogIntake logs (filtered)', 'logintake', '/api/LogIntake/logs?speciesId=1&lengthId=1'),
    sampler('read', 'Sawmill stock (proxy to LogIntake)', 'sawmill', '/api/Sawmill/stock'),
    sampler('read', 'Sawmill stock logs (proxy)', 'sawmill', '/api/Sawmill/stock/1/logs'),
    sampler('read', 'Sawmill machines', 'sawmill', '/api/Sawmill/machines'),
    sampler('read', 'Sawmill workers', 'sawmill', '/api/Sawmill/workers'),
    sampler('read', 'Sawmill recent jobs', 'sawmill', '/api/Sawmill/jobs'),
    sampler('read', 'Treatment sawn stock', 'treatment', '/api/TreatmentStock'),
    sampler('read', 'Treatment treated stock', 'treatment', '/api/TreatmentStock/treated'),
    sampler('read', 'Treatment tanks', 'treatment', '/api/Tanks/availability'),
    sampler('read', 'Treatment batches (Completed)', 'treatment', '/api/TreatmentBatches?status=Completed'),
    sampler('read', 'Treatment batch by id', 'treatment', '/api/TreatmentBatches/1'),
]
REPORTS = [  # Manager token: Supervisor would get 403
    sampler('report', 'Treatment duration report', 'treatment', f'/api/TreatmentBatches/reports/duration?from={FROM}&to={TO}', token=TOK_MGR, contains='completedBatches'),
    sampler('report', 'Sawmill wastage-yield report', 'sawmill', f'/api/Sawmill/jobs/wastage-yield-report?from={FROM}&to={TO}', token=TOK_MGR),
    sampler('report', 'Sawmill job history', 'sawmill', f'/api/Sawmill/jobs/history?from={FROM}&to={TO}', token=TOK_MGR),
    sampler('report', 'Treatment low-stock alerts', 'treatment', '/api/TreatmentStock/treated/alerts', token=TOK_MGR),
    sampler('report', 'LogIntake deliveries (full list)', 'logintake', '/api/LogIntake/deliveries', token=TOK_MGR),
]
SUPPLIER = '{"name":"perf-load ${__UUID()}","phoneNumber":"+94112345678","address":"perf-load"}'
DELIVERY = ('{"supplierId":${SUPPLIER_ID},"vehicleNumber":"PERF-LOAD","receivedBy":1,"notes":"perf-load","logs":'
            '[{"speciesId":1,"lengthId":1,"girthFt":3.5},{"speciesId":2,"lengthId":3,"girthFt":4.0},{"speciesId":3,"lengthId":5,"girthFt":4.5}]}')
WRITES = [
    sampler('write', 'LogIntake create supplier', 'logintake', '/api/LogIntake/suppliers', 'POST', 201, SUPPLIER, extract=('SUPPLIER_ID', '$.supplierId')),
    sampler('write', 'LogIntake record delivery', 'logintake', '/api/LogIntake/deliveries', 'POST', 201, DELIVERY),
]
LOGINS = [
    sampler('login', 'auth login', 'auth', '/auth/login', 'POST', 200, '{"username":"load_supervisor","password":"${__P(password,LoadTest123!)}"}', contains='token'),
    sampler('read', 'no token -> 401', 'logintake', '/api/LogIntake/stock', expect=401, token='none'),
    sampler('read', 'supervisor on manager report -> 403', 'treatment', '/api/TreatmentBatches/reports/duration', expect=403),
    sampler('login', 'auth login bad password -> 401', 'auth', '/auth/login', 'POST', 401, '{"username":"load_supervisor","password":"definitely-wrong"}'),
]


def thread_group(kind, name, threads, ramp, duration, delay, body):
    cls = 'SetupThreadGroup' if kind == 'setup' else 'ThreadGroup'
    gui = 'SetupThreadGroupGui' if kind == 'setup' else 'ThreadGroupGui'
    err = 'stoptest' if kind == 'setup' else 'continue'
    sched = 'false' if kind == 'setup' else 'true'
    loops = '1' if kind == 'setup' else '-1'
    return (f'<{cls} guiclass="{gui}" testclass="{cls}" testname="{name}" enabled="true">'
            f'<stringProp name="ThreadGroup.on_sample_error">{err}</stringProp>'
            '<elementProp name="ThreadGroup.main_controller" elementType="LoopController" guiclass="LoopControlPanel" testclass="LoopController" enabled="true">'
            f'<boolProp name="LoopController.continue_forever">false</boolProp><stringProp name="LoopController.loops">{loops}</stringProp></elementProp>'
            f'<stringProp name="ThreadGroup.num_threads">{threads}</stringProp><stringProp name="ThreadGroup.ramp_time">{ramp}</stringProp>'
            f'<boolProp name="ThreadGroup.scheduler">{sched}</boolProp><stringProp name="ThreadGroup.duration">{duration}</stringProp>'
            f'<stringProp name="ThreadGroup.delay">{delay}</stringProp><boolProp name="ThreadGroup.same_user_on_next_iteration">true</boolProp>'
            f'</{cls}><hashTree>{body}</hashTree>')


def setup_group():
    # sampler() ends with ...</HTTPSamplerProxy><hashTree>KIDS</hashTree>; append the JSR223 inside that last hashTree
    def one(label, user, prop):
        s = sampler('setup', label, 'auth', '/auth/login', 'POST', 200,
                    f'{{"username":"{user}","password":"${{__P(password,LoadTest123!)}}"}}', contains='token')
        jsr = ('<JSR223PostProcessor guiclass="TestBeanGUI" testclass="JSR223PostProcessor" testname="Store token as property" enabled="true">'
               '<stringProp name="scriptLanguage">groovy</stringProp><stringProp name="parameters"></stringProp><stringProp name="filename"></stringProp>'
               '<stringProp name="cacheKey">true</stringProp>'
               f'<stringProp name="script">props.put("{prop}", new groovy.json.JsonSlurper().parseText(prev.getResponseDataAsString()).token)</stringProp>'
               '</JSR223PostProcessor><hashTree/>')
        return s[:-len('</hashTree>')] + jsr + '</hashTree>'
    body = (hdr('JSON', [('Content-Type', 'application/json')]) +
            one('login supervisor', 'load_supervisor', 'TOKEN_SUP') +
            one('login manager', 'load_manager', 'TOKEN_MGR'))
    return thread_group('setup', 'setUp - log in once', '1', '1', '', '', body)


def wave(i):
    default_threads = '0'
    flow = (hdr('JSON + supervisor token', [('Content-Type', 'application/json'), ('Authorization', f'Bearer {TOK_SUP}')]) +
            f'<UniformRandomTimer guiclass="UniformRandomTimerGui" testclass="UniformRandomTimer" testname="Think time" enabled="true">'
            '<stringProp name="ConstantTimer.delay">${__P(think.min,300)}</stringProp>'
            '<stringProp name="RandomTimer.range">${__P(think.range,600)}</stringProp></UniformRandomTimer><hashTree/>'
            '<RandomController guiclass="RandomControlGui" testclass="RandomController" testname="Weighted user action (40% browse, 20% reports, 20% writes, 20% auth)" enabled="true">'
            '<intProp name="InterleaveControl.style">0</intProp></RandomController><hashTree>' +
            controller('Browse screens', BROWSE) + controller('Browse screens', BROWSE) +
            controller('Manager reports', REPORTS) + controller('Record data', WRITES) + controller('Auth + access control', LOGINS) +
            '</hashTree>')
    p = f'wave{i}'
    return thread_group('normal', f'Users wave {i}', f'${{__P({p}.threads,{default_threads})}}', f'${{__P({p}.ramp,10)}}',
                        f'${{__P({p}.duration,60)}}', f'${{__P({p}.delay,0)}}', flow)


def plan():
    groups = setup_group() + ''.join(wave(i) for i in range(1, WAVES + 1))
    return ('<?xml version="1.0" encoding="UTF-8"?>\n<jmeterTestPlan version="1.2" properties="5.0" jmeter="5.6.3"><hashTree>'
            '<TestPlan guiclass="TestPlanGui" testclass="TestPlan" testname="Timber Yard - Load &amp; Performance" enabled="true">'
            '<stringProp name="TestPlan.comments">One plan for smoke/load/stress/spike/soak: pick a profile with -q profiles/NAME.properties. '
            'Generated by build_plan.py - edit that, not this file.</stringProp>'
            '<boolProp name="TestPlan.functional_mode">false</boolProp><boolProp name="TestPlan.serialize_threadgroups">false</boolProp>'
            '<elementProp name="TestPlan.user_defined_variables" elementType="Arguments" guiclass="ArgumentsPanel" testclass="Arguments" enabled="true">'
            '<collectionProp name="Arguments.arguments"/></elementProp></TestPlan><hashTree>' + groups + '</hashTree></hashTree></jmeterTestPlan>\n')


if __name__ == '__main__':
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'timber-yard-load.jmx')
    open(out, 'w', encoding='utf-8').write(plan())
    print('wrote', out)
