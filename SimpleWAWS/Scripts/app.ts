﻿angular.module("tryApp", ["ui.router"])
    .config(["$stateProvider", "$urlRouterProvider", "$locationProvider", ($stateProvider: ng.ui.IStateProvider, $urlRouterProvider: ng.ui.IUrlRouterProvider, $locationProvider: ng.ILocationProvider) => {
    var homeState: ng.ui.IState = {
        name: "home",
        url: "/",
        //abstract: true,
        templateUrl: "templates/steps.html",
        controller: "appController"
    };

    var webApps: ng.ui.IState[] = [{
        name: "home.webapp",
        url: "webapp",
        templateUrl: "templates/empty-shell.html"
    }, {
            name: "home.webapp.templates",
            templateUrl: "templates/templates.html",
            url: "/templates?language&name"
        }, {
            name: "home.webapp.work",
            templateUrl: "templates/work.html",
            url: "/work"
        }];

    var mobileApps: ng.ui.IState[] = [{
        name: "home.mobileapp",
        templateUrl: "templates/empty-shell.html",
        url: "mobileapp",
    }, {
            name: "home.mobileapp.templates",
            templateUrl: "templates/templates.html",
            url: "/templates?language&name"
        }, {
            name: "home.mobileapp.clients",
            templateUrl: "templates/clients.html",
            url: "/clients"
        }, {
            name: "home.mobileapp.work",
            templateUrl: "templates/work.html",
            url: "/work"
        }];

    var apiApps: ng.ui.IState[] = [{
        name: "home.apiapp",
        templateUrl: "/templates/empty-shell.html",
        url: "apiapp",
    }, {
            name: "home.apiapp.templates",
            templateUrl: "/templates/templates.html",
            url: "/templates?language&name"
        }, {
            name: "home.apiapp.work",
            templateUrl: "/templates/work.html",
            url: "/work"
        }];

    var logicApps: ng.ui.IState[] = [{
        name: "home.logicapp",
        templateUrl: "templates/empty-shell.html",
        url: "logicapp"
    }, {
            name: "home.logicapp.comingsoon",
            templateUrl: "templates/comingsoon.html",
            url: "logicapp/comingsoon"
        }];
    $stateProvider.state(homeState);
    webApps.forEach(s => $stateProvider.state(s));
    mobileApps.forEach(s => $stateProvider.state(s));
    apiApps.forEach(s => $stateProvider.state(s));
    logicApps.forEach(s => $stateProvider.state(s));

    $urlRouterProvider.otherwise("/webapp");
    $locationProvider.html5Mode(true);

}])
    .controller("appController", ["$scope", "$http", "$timeout", "$rootScope", "$state", function ($scope: IAppControllerScope, $http: ng.IHttpService, $timeout: ng.ITimeoutService, $rootScope: ng.IRootScopeService, $state: ng.ui.IStateService) {

    $scope.appServices = [{
        name: "webapp",
        sprite: "sprite-AzureWebsites",
        title: "Web App",
        steps: [{
            id: 1,
            title: "Select your App Service",
            sref: "home",
        }, {
                id: 2,
                title: "Select template and create",
                sref: "home.webapp.templates",
                nextClass: "wa-button-primary",
                nextText: "Create"
            }, {
                id: 3,
                title: "Work with your app",
                sref: "home.webapp.work",
            }],
        templates: [{
            name: "Empty Site",
            sprite: "sprite-Large",

        }]
    }, {
            name: "mobileapp",
            sprite: "sprite-MobileServices",
            title: "Mobile App",
            steps: [{
                id: 1,
                title: "Select your App Service",
                sref: "home",
            }, {
                    id: 2,
                    title: "Select template and create",
                    sref: "home.mobileapp.templates",
                    nextClass: "wa-button-primary",
                    nextText: "Create"
                }, {
                    id: 3,
                    title: "download your client",
                    sref: "home.mobileapp.clients",
                }, {
                    id: 4,
                    title: "Work with your app",
                    sref: "home.mobileapp.work",
                }],
            templates: [{
                name: "Todo List",
                sprite: "sprite-todolist",
                appService: "mobile"
            }]
        }, {
            name: "apiapp",
            sprite: "sprite-APIApps",
            title: "API App",
            steps: [{
                id: 1,
                title: "Select your App Service",
                sref: "home",
            }, {
                    id: 2,
                    title: "Select template and create",
                    sref: "home.apiapp.templates",
                    nextClass: "wa-button-primary",
                    nextText: "Create"
                }, {
                    id: 3,
                    title: "Work with your app",
                    sref: "home.apiapp.work",
                }],
            templates: []
        }, {
            name: "logicapp",
            sprite: "sprite-LogicApp",
            title: "Logic App",
            steps: [{
                id: 1,
                title: "Select your App Service",
                sref: "home"
            }, {
                    id: 2,
                    title: "Coming Soon",
                    sref: "home.logicapp.comingsoon"
                }],
            templates: []
        }];
    $scope.currentAppService = $scope.appServices[0];

    $scope.selectAppService = (appService) => {
        $scope.currentAppService = appService;
        $scope.setNextAndPreviousSteps(0);
    };
    $scope.nextState = (index) => {
        if (index < $scope.currentAppService.steps.length) {
            return $scope.currentAppService.steps[index].sref;
        }
        return undefined;
    };

    $scope.setNextAndPreviousSteps = (index) => {
        $scope.currentStep = $scope.currentAppService.steps[index];
        $scope.nextStep = $scope.currentAppService.steps[index + 1];
        $scope.previousStep = $scope.currentAppService.steps[index - 1];
    };

    $scope.setNextAndPreviousSteps(0);

    $rootScope.$on('$stateChangeStart',
        (event, toState, toParams, fromState, fromParams) => {
            $timeout(() => {
                console.log(toState);
                var step = $scope.currentAppService.steps.find((s) => s.sref === toState.name);
                $scope.setNextAndPreviousSteps(step.id - 1);
                // transitionTo() promise will be rejected with 
                // a 'transition prevented' error
            });
        });

    $scope.getStateLink = (step) => {
        return $state.href(step.sref);
    };



}]).run(function ($rootScope, $state, $stateParams) {
    $rootScope.$state = $state;
    $rootScope.$stateParams = $stateParams;
});